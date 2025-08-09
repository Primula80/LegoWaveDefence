// Unity 6 Editor-only server that exposes safe localhost endpoints, supports self-extension
// via [AiRoute] + /write_file, persistent memory helpers, console capture, test running,
// a panic toggle, and an index_project snapshotter.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AiRouteAttribute : Attribute
{
    public string Path { get; }
    public AiRouteAttribute(string path) => Path = path.ToLowerInvariant();
}

[InitializeOnLoad]
public static class AiAutomationServer
{
    // -------------------- Config --------------------
    private const string k_Prefix = "http://127.0.0.1:17890/";
    private const string k_HeaderToken = "X-AI-Token";
    private static string _token =
        Environment.GetEnvironmentVariable("AI_AUTOMATION_TOKEN", EnvironmentVariableTarget.Process)
        ?? Environment.GetEnvironmentVariable("AI_AUTOMATION_TOKEN", EnvironmentVariableTarget.User)
        ?? Environment.GetEnvironmentVariable("AI_AUTOMATION_TOKEN", EnvironmentVariableTarget.Machine)
        ?? "CHANGE_ME";

    // Panic toggle (menu controlled)
    static bool AI_ENABLED = true;

    // Whitelisted folder the AI can write new endpoint files into
    private static readonly string k_GeneratedRoot = Path.Combine(
        Directory.GetCurrentDirectory(),
        "Assets/Packages/com.yourco.ai-automation/Editor/Generated");

    static HttpListener _listener;
    static System.Threading.Thread _thread;
    static readonly Dictionary<string, MethodInfo> _routes = new(StringComparer.OrdinalIgnoreCase);

    static AiAutomationServer()
    {
        try { Directory.CreateDirectory(k_GeneratedRoot); } catch { }
        EditorApplication.delayCall += Start;
        EditorApplication.quitting += Stop;

        IndexRoutes();
        AssemblyReloadEvents.afterAssemblyReload += IndexRoutes;
    }

    [MenuItem("AI Automation/Toggle Server")]
    static void ToggleServer()
    {
        if (_listener == null) Start();
        else Stop();
    }

    [MenuItem("AI Automation/AI Enabled", true)]
    static bool AiEnabledValidate()
    {
        Menu.SetChecked("AI Automation/AI Enabled", AI_ENABLED);
        return true;
    }
    [MenuItem("AI Automation/AI Enabled")]
    static void AiEnabledToggle()
    {
        AI_ENABLED = !AI_ENABLED;
        Menu.SetChecked("AI Automation/AI Enabled", AI_ENABLED);
        Debug.Log($"[AI Server] AI_ENABLED = {AI_ENABLED}");
    }

    [MenuItem("AI Automation/Set Token To Clipboard Value")]
    static void SetTokenFromClipboard()
    {
        var tok = GUIUtility.systemCopyBuffer?.Trim();
        if (!string.IsNullOrEmpty(tok))
        {
            _token = tok;
            EditorPrefs.SetString("AI_AUTOMATION_TOKEN", tok);
            Debug.Log("[AI Server] Token set from clipboard.");
        }
        else Debug.LogWarning("[AI Server] Clipboard empty.");
    }

    static void Start()
    {
        try
        {
            if (_listener != null) return;

            var pref = EditorPrefs.GetString("AI_AUTOMATION_TOKEN", null);
            if (!string.IsNullOrEmpty(pref)) _token = pref;

            _listener = new HttpListener();
            _listener.Prefixes.Add(k_Prefix);
            _listener.Start();
            _thread = new System.Threading.Thread(Loop) { IsBackground = true };
            _thread.Start();
            Debug.Log($"[AI Server] Listening at {k_Prefix}, routes={_routes.Count}");
        }
        catch (Exception e) { Debug.LogError($"[AI Server] Start failed: {e}"); Stop(); }
    }

    static void Stop()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
        try { _thread?.Abort(); } catch { }
        _thread = null;
        Debug.Log("[AI Server] Stopped");
    }

    static void Loop()
    {
        while (_listener != null && _listener.IsListening)
        {
            try
            {
                var ctx = _listener.GetContext();
                EditorApplication.delayCall += () => Handle(ctx);
            }
            catch { /* listener closed */ }
        }
    }

    static void Handle(HttpListenerContext ctx)
    {
        try
        {
            // Panic gate
            if (!AI_ENABLED) { Write(ctx, 503, JsonErr("disabled")); return; }

            // Auth
            var sent = ctx.Request.Headers[k_HeaderToken];
            if (string.IsNullOrEmpty(sent) || !string.Equals(sent, _token))
            {
                Write(ctx, 401, JsonErr("unauthorized"));
                return;
            }

            var route = ctx.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();

            // Built-ins
            if (route == "ping") { Write(ctx, 200, JsonOk("pong")); return; }
            if (route == "reload_routes") { IndexRoutes(); Write(ctx, 200, JsonOk($"reloaded:{_routes.Count}")); return; }
            if (route == "get_state") { Write(ctx, 200, JsonOkRaw(GetStateJson())); return; }
            if (route == "wait_compilation") { WaitCompilation(); Write(ctx, 200, JsonOk("compiled")); return; }
            if (route == "panic_on") { AI_ENABLED = false; Write(ctx, 200, JsonOk("off")); return; }
            if (route == "panic_off") { AI_ENABLED = true; Write(ctx, 200, JsonOk("on")); return; }
            if (route == "index_project") { Write(ctx, 200, IndexProject()); return; }

            // Read body
            string body = "";
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                body = reader.ReadToEnd();

            // Dynamic routes
            if (_routes.TryGetValue(route, out var method))
            {
                var res = (string)method.Invoke(null, new object[] { body });
                Write(ctx, 200, res);
                TryAutoCommit($"AI:{route}");
                return;
            }

            // Core ops
            string result = route switch
            {
                "open_scene" => OpenScene(body),
                "save_scene" => SaveScene(),
                "create_game_object" => CreateGameObject(body),
                "add_component" => AddComponent(body),
                "set_property" => SetProperty(body),
                "set_transform" => SetTransform(body),
                "create_prefab" => CreatePrefab(body),
                "link_reference" => LinkReference(body),
                "execute_menu_item" => ExecuteMenuItem(body),
                "write_file" => WriteFile(body),

                "mem_append" => MemAppend(body),
                "mem_kv_set" => MemKvSet(body),
                "mem_kv_get" => MemKvGet(body),

                "get_console" => GetConsole(body),
                "run_tests" => RunTests(body),

                "set_build_scenes" => SetBuildScenes(body),
                "build_player" => BuildPlayer(body),

                _ => JsonErr($"unknown route: {route}")
            };

            Write(ctx, 200, result);
            TryAutoCommit($"AI:{route}");
        }
        catch (Exception e)
        {
            Write(ctx, 500, JsonErr(e.ToString()));
        }
    }

    // -------------------- Route indexing --------------------
    [InitializeOnLoadMethod]
    static void IndexRoutes()
    {
        _routes.Clear();
        try
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.FullName.Contains("Editor"));

            foreach (var asm in asms)
                foreach (var mi in asm.GetTypes()
                             .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)))
                {
                    var attr = mi.GetCustomAttribute<AiRouteAttribute>();
                    if (attr == null) continue;
                    _routes[attr.Path] = mi;
                }
        }
        catch (Exception e) { Debug.LogError($"[AI Server] IndexRoutes error: {e}"); }

        Debug.Log($"[AI Server] Indexed routes: {_routes.Count}");
    }

    // -------------------- Built-in endpoints --------------------
    [Serializable] class OpenSceneReq { public string path; }
    static string OpenScene(string json)
    {
        var req = JsonUtility.FromJson<OpenSceneReq>(json);
        if (string.IsNullOrEmpty(req.path) || !File.Exists(req.path)) return JsonErr($"scene not found: {req.path}");
        var scene = EditorSceneManager.OpenScene(req.path, OpenSceneMode.Single);
        return JsonOk(scene.path);
    }

    static string SaveScene()
    {
        var s = SceneManager.GetActiveScene();
        if (!s.IsValid()) return JsonErr("no active scene");
        EditorSceneManager.SaveScene(s);
        AssetDatabase.SaveAssets();
        return JsonOk(s.path);
    }

    [Serializable] class CreateGoReq { public string name; public string parent; }
    static string CreateGameObject(string json)
    {
        var req = JsonUtility.FromJson<CreateGoReq>(json);
        var go = new GameObject(string.IsNullOrEmpty(req.name) ? "GameObject" : req.name);
        if (!string.IsNullOrEmpty(req.parent))
        {
            var parent = GameObject.Find(req.parent);
            if (parent) go.transform.SetParent(parent.transform, false);
        }
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return JsonOk(go.name);
    }

    [Serializable] class AddCompReq { public string path; public string type; }
    static string AddComponent(string json)
    {
        var req = JsonUtility.FromJson<AddCompReq>(json);
        var go = GameObject.Find(req.path);
        if (!go) return JsonErr($"gameobject not found: {req.path}");
        var t = Type.GetType(req.type);
        if (t == null) return JsonErr($"type not found: {req.type}");
        var comp = go.GetComponent(t) ?? go.AddComponent(t);
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return JsonOk(comp.GetType().FullName);
    }

    [Serializable] class SetTransformReq { public string path; public float[] position; public float[] rotation; public float[] scale; }
    static string SetTransform(string json)
    {
        var req = JsonUtility.FromJson<SetTransformReq>(json);
        var go = GameObject.Find(req.path);
        if (!go) return JsonErr($"gameobject not found: {req.path}");
        if (req.position is { Length: 3 }) go.transform.position = new Vector3(req.position[0], req.position[1], req.position[2]);
        if (req.rotation is { Length: 3 }) go.transform.eulerAngles = new Vector3(req.rotation[0], req.rotation[1], req.rotation[2]);
        if (req.scale is { Length: 3 }) go.transform.localScale = new Vector3(req.scale[0], req.scale[1], req.scale[2]);
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return JsonOk("ok");
    }

    [Serializable] class CreatePrefabReq { public string from; public string to; }
    static string CreatePrefab(string json)
    {
        var req = JsonUtility.FromJson<CreatePrefabReq>(json);
        var go = GameObject.Find(req.from);
        if (!go) return JsonErr($"gameobject not found: {req.from}");
        var dir = Path.GetDirectoryName(req.to);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        PrefabUtility.SaveAsPrefabAsset(go, req.to);
        AssetDatabase.SaveAssets();
        return JsonOk(AssetDatabase.AssetPathToGUID(req.to));
    }

    [Serializable] class SetPropReq { public string path; public string component; public string property; public string value; }
    static string SetProperty(string json)
    {
        var req = JsonUtility.FromJson<SetPropReq>(json);
        var go = GameObject.Find(req.path);
        if (!go) return JsonErr($"gameobject not found: {req.path}");
        var t = Type.GetType(req.component);
        if (t == null) return JsonErr($"type not found: {req.component}");
        var comp = go.GetComponent(t);
        if (!comp) return JsonErr($"component missing: {req.component}");

        var so = new SerializedObject(comp);
        var sp = so.FindProperty(req.property);
        if (sp == null) return JsonErr($"property not found: {req.property}");

        if (float.TryParse(req.value, out var f) && sp.propertyType == SerializedPropertyType.Float) sp.floatValue = f;
        else if (int.TryParse(req.value, out var i) && sp.propertyType == SerializedPropertyType.Integer) sp.intValue = i;
        else if (bool.TryParse(req.value, out var b) && sp.propertyType == SerializedPropertyType.Boolean) sp.boolValue = b;
        else if (sp.propertyType == SerializedPropertyType.String) sp.stringValue = req.value;
        else return JsonErr($"unsupported property type for '{req.property}'");

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return JsonOk("ok");
    }

    [Serializable] class LinkRefReq { public string target; public string component; public string property; public string source; }
    static string LinkReference(string json)
    {
        var req = JsonUtility.FromJson<LinkRefReq>(json);
        var targetGo = GameObject.Find(req.target);
        var sourceGo = GameObject.Find(req.source);
        if (!targetGo) return JsonErr($"target not found: {req.target}");
        if (!sourceGo) return JsonErr($"source not found: {req.source}");
        var t = Type.GetType(req.component);
        if (t == null) return JsonErr($"type not found: {req.component}");
        var comp = targetGo.GetComponent(t);
        if (!comp) return JsonErr($"component missing: {req.component}");

        var so = new SerializedObject(comp);
        var sp = so.FindProperty(req.property);
        if (sp == null) return JsonErr($"property not found: {req.property}");
        if (sp.propertyType != SerializedPropertyType.ObjectReference)
            return JsonErr($"property '{req.property}' is not an object reference");

        sp.objectReferenceValue = sourceGo;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(targetGo);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return JsonOk("ok");
    }

    [Serializable] class MenuReq { public string menu; }
    static string ExecuteMenuItem(string json)
    {
        var req = JsonUtility.FromJson<MenuReq>(json);
        var ok = EditorApplication.ExecuteMenuItem(req.menu);
        return ok ? JsonOk("ok") : JsonErr($"menu failed: {req.menu}");
    }

    // ---- Write code files into the whitelist, supports raw or base64 content ----
    [Serializable] class WriteReq { public string relPath; public string contents; public string contentsBase64; public bool overwrite = true; }

    [AiRoute("write_file")]
    public static string WriteFile(string json)
    {
        var req = JsonUtility.FromJson<WriteReq>(json);
        Directory.CreateDirectory(k_GeneratedRoot);
        var safeRel = string.IsNullOrEmpty(req.relPath) ? "NewFile.cs" : req.relPath.Replace('\\', '/');
        var full = Path.GetFullPath(Path.Combine(k_GeneratedRoot, safeRel));
        if (!full.StartsWith(k_GeneratedRoot, StringComparison.OrdinalIgnoreCase))
            return JsonErr("path outside allowed folder");
        if (File.Exists(full) && !req.overwrite) return JsonErr("file exists and overwrite=false");

        var bytes = !string.IsNullOrEmpty(req.contentsBase64)
            ? Convert.FromBase64String(req.contentsBase64)
            : Encoding.UTF8.GetBytes(req.contents ?? "");
        File.WriteAllBytes(full, bytes);

        var assetRel = full[(Directory.GetCurrentDirectory().Length + 1)..].Replace('\\', '/');
        AssetDatabase.ImportAsset(assetRel);
        AssetDatabase.Refresh();

        return JsonOk(assetRel);
    }

    // -------------------- Memory endpoints --------------------
    [Serializable] class MemWriteReq { public string file; public string lineJson; }
    [Serializable] class MemKvSetReq { public string key; public string value; }
    [Serializable] class MemKvGetReq { public string key; }

    static string MemoryDir()
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "AI", "memory");
        Directory.CreateDirectory(root);
        return root;
    }

    [AiRoute("mem_append")]
    public static string MemAppend(string json)
    {
        var req = JsonUtility.FromJson<MemWriteReq>(json);
        var fname = string.IsNullOrEmpty(req.file) ? "journal.jsonl" : req.file;
        var full = Path.Combine(MemoryDir(), fname);
        File.AppendAllText(full, (req.lineJson?.Trim() ?? "{}") + Environment.NewLine, Encoding.UTF8);
        AssetDatabase.Refresh();
        return JsonOk(full);
    }

    [AiRoute("mem_kv_set")]
    public static string MemKvSet(string json)
    {
        var req = JsonUtility.FromJson<MemKvSetReq>(json);
        var full = Path.Combine(MemoryDir(), "kv.json");
        var map = new Dictionary<string, string>();
        if (File.Exists(full))
        {
            try { map = JsonUtility.FromJson<DictWrapper>(File.ReadAllText(full, Encoding.UTF8))?.ToDict() ?? new(); }
            catch { map = new(); }
        }
        map[req.key] = req.value;
        File.WriteAllText(full, JsonUtility.ToJson(DictWrapper.From(map), true), Encoding.UTF8);
        AssetDatabase.Refresh();
        return JsonOk(req.key);
    }

    [AiRoute("mem_kv_get")]
    public static string MemKvGet(string json)
    {
        var req = JsonUtility.FromJson<MemKvGetReq>(json);
        var full = Path.Combine(MemoryDir(), "kv.json");
        if (!File.Exists(full)) return JsonOk("");
        var map = JsonUtility.FromJson<DictWrapper>(File.ReadAllText(full, Encoding.UTF8))?.ToDict() ?? new();
        map.TryGetValue(req.key, out var val);
        return JsonOk(val ?? "");
    }

    // -------------------- Console capture --------------------
    [Serializable] class ConsoleReq { public int limit = 200; public string level = "all"; }

    [AiRoute("get_console")]
    public static string GetConsole(string json)
    {
        var req = JsonUtility.FromJson<ConsoleReq>(json);
        int max = Mathf.Clamp(req.limit, 1, 5000);

        var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor");
        var logEntryType = Type.GetType("UnityEditor.LogEntry, UnityEditor");
        if (logEntriesType == null || logEntryType == null) return JsonErr("Cannot access UnityEditor.LogEntries");

        var GetCount = logEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.Static);
        var StartGettingEntries = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Public | BindingFlags.Static);
        var GetEntryInternal = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Public | BindingFlags.Static);
        var EndGettingEntries = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Public | BindingFlags.Static);

        int count = (int)GetCount.Invoke(null, null);
        int take = Math.Min(max, count);

        var entries = new List<Dictionary<string, object>>();
        int errors = 0, warnings = 0, logs = 0;

        StartGettingEntries.Invoke(null, null);
        try
        {
            var entry = Activator.CreateInstance(logEntryType);
            for (int i = Math.Max(0, count - take); i < count; i++)
            {
                GetEntryInternal.Invoke(null, new object[] { i, entry });
                var condition = (string)logEntryType.GetField("condition").GetValue(entry);
                var file = (string)logEntryType.GetField("file").GetValue(entry);
                var line = (int)logEntryType.GetField("line").GetValue(entry);
                var mode = (int)logEntryType.GetField("mode").GetValue(entry);
                var stack = (string)logEntryType.GetField("stacktrace").GetValue(entry);

                string level = (mode & 16) != 0 ? "warning"
                             : (mode & 1) != 0 ? "error"
                             : "log";

                if (level == "error") errors++; else if (level == "warning") warnings++; else logs++;

                if (req.level == "error" && level != "error") continue;
                if (req.level == "warning" && level != "warning") continue;
                if (req.level == "log" && level != "log") continue;

                entries.Add(new Dictionary<string, object>
                {
                    ["level"] = level,
                    ["message"] = condition,
                    ["file"] = file,
                    ["line"] = line,
                    ["mode"] = mode,
                    ["stack"] = stack
                });
            }
        }
        finally { EndGettingEntries.Invoke(null, null); }

        var summary = new Dictionary<string, object>
        {
            ["total"] = count,
            ["returned"] = entries.Count,
            ["errors"] = errors,
            ["warnings"] = warnings,
            ["logs"] = logs
        };

        var payload = new Dictionary<string, object> { ["summary"] = summary, ["entries"] = entries };
        return JsonOkRaw(MiniJson.Serialize(payload));
    }

    // -------------------- Test runner --------------------
    [Serializable] class TestReq { public string mode = "editmode"; public int timeoutSec = 300; } // mode: editmode|playmode|all

    [AiRoute("run_tests")]
    public static string RunTests(string json)
    {
        var req = JsonUtility.FromJson<TestReq>(json);
        var api = new TestRunnerApi();

        var filter = new Filter();
        if (req.mode == "editmode") filter.testMode = TestMode.EditMode;
        else if (req.mode == "playmode") filter.testMode = TestMode.PlayMode;
        else filter.testMode = TestMode.EditMode | TestMode.PlayMode;

        var finished = false;
        int passed = 0, failed = 0, skipped = 0;
        var results = new List<Dictionary<string, object>>();

        api.RegisterCallbacks(new Callbacks
        {
            runStarted = _ => { },
            testStarted = _ => { },
            testFinished = r =>
            {
                var status = r.TestStatus; // TestStatus
                var state = status.ToString();

                if (status == TestStatus.Passed) passed++;
                else if (status == TestStatus.Failed) failed++;
                else skipped++;

                results.Add(new Dictionary<string, object>
                {
                    ["name"] = r.Name,
                    ["fullName"] = r.FullName,
                    ["duration"] = r.Duration,
                    ["status"] = state,
                    ["message"] = r.Message,
                    ["stacktrace"] = r.StackTrace
                });
            },
            runFinished = _ => { finished = true; }
        });

        api.Execute(new ExecutionSettings(filter));

        double start = EditorApplication.timeSinceStartup;
        while (!finished)
        {
            if (EditorApplication.timeSinceStartup - start > Mathf.Max(30, req.timeoutSec)) break;
            System.Threading.Thread.Sleep(100);
        }

        var payload = new Dictionary<string, object>
        {
            ["summary"] = new Dictionary<string, object>
            {
                ["passed"] = passed,
                ["failed"] = failed,
                ["skipped"] = skipped
            },
            ["results"] = results
        };
        return JsonOkRaw(MiniJson.Serialize(payload));
    }

    // -------------------- Project index --------------------
    [Serializable] class IndexReq { public bool writeToDisk = true; }
    static string IndexProject()
    {
        var scenes = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
        var prefabs = AssetDatabase.FindAssets("t:Prefab").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();

        // Try Addressables via reflection (optional)
        var addr = new Dictionary<string, object>();
        try
        {
            var settingsType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            var prop = settingsType?.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
            var settings = prop?.GetValue(null);
            if (settings != null)
            {
                var findGroups = settings.GetType().GetProperty("groups");
                var groups = (System.Collections.IEnumerable)findGroups.GetValue(settings);
                var groupsList = new List<Dictionary<string, object>>();
                foreach (var g in groups)
                {
                    var gName = g.GetType().GetProperty("Name").GetValue(g)?.ToString() ?? "";
                    var entriesProp = g.GetType().GetProperty("entries");
                    var entries = (System.Collections.IEnumerable)entriesProp.GetValue(g);
                    var paths = new List<string>();
                    foreach (var e in entries)
                    {
                        var guidProp = e.GetType().GetProperty("guid");
                        var guid = guidProp?.GetValue(e)?.ToString();
                        if (!string.IsNullOrEmpty(guid))
                        {
                            var p = AssetDatabase.GUIDToAssetPath(guid);
                            if (!string.IsNullOrEmpty(p)) paths.Add(p);
                        }
                    }
                    groupsList.Add(new Dictionary<string, object> { { "group", gName }, { "paths", paths } });
                }
                addr["groups"] = groupsList;
            }
        }
        catch { /* addressables not present */ }

        var payload = new Dictionary<string, object>
        {
            ["scenes"] = scenes,
            ["prefabs"] = prefabs,
            ["addressables"] = addr
        };

        // write to AI/memory/index.json
        var mem = MemoryDir();
        var full = Path.Combine(mem, "index.json");
        try { File.WriteAllText(full, MiniJson.Serialize(payload), Encoding.UTF8); AssetDatabase.Refresh(); } catch { }

        return JsonOkRaw(MiniJson.Serialize(payload));
    }

    // -------------------- Build helpers --------------------
    [Serializable] class SetBuildScenesReq { public string[] scenePaths; }
    static string SetBuildScenes(string json)
    {
        var req = JsonUtility.FromJson<SetBuildScenesReq>(json);
        if (req.scenePaths == null || req.scenePaths.Length == 0) return JsonErr("no scenes");
        var list = new List<EditorBuildSettingsScene>();
        foreach (var p in req.scenePaths)
        {
            if (!File.Exists(p)) return JsonErr("missing scene: " + p);
            list.Add(new EditorBuildSettingsScene(p, true));
        }
        EditorBuildSettings.scenes = list.ToArray();
        return JsonOk("ok");
    }

    [Serializable] class BuildReq { public string outputPath; public string target; public bool development = true; }
    static string BuildPlayer(string json)
    {
        var req = JsonUtility.FromJson<BuildReq>(json);
        var target = BuildTarget.StandaloneWindows64;
        if (!string.IsNullOrEmpty(req.target)) Enum.TryParse(req.target, out target);
        var opts = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = req.outputPath,
            target = target,
            options = req.development ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            return JsonErr($"build failed: {report.summary.result}");
        return JsonOk(req.outputPath);
    }

    // -------------------- Helpers --------------------
    static string GetStateJson()
    {
        var compiling = EditorApplication.isCompiling || EditorApplication.isUpdating;
        return $"{{\"compiling\":{compiling.ToString().ToLower()},\"routes\":{_routes.Count},\"enabled\":{AI_ENABLED.ToString().ToLower()}}}";
    }

    static void WaitCompilation()
    {
        double start = EditorApplication.timeSinceStartup;
        while (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (EditorApplication.timeSinceStartup - start > 60) break;
            System.Threading.Thread.Sleep(100);
        }
        IndexRoutes();
    }

    static void Write(HttpListenerContext ctx, int code, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentEncoding = Encoding.UTF8;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    static void TryAutoCommit(string message)
    {
        try { RunGit("add -A"); RunGit($"commit -m \"{message}\""); } catch { }
    }

    static void RunGit(string args)
    {
        var p = new System.Diagnostics.Process();
        p.StartInfo.FileName = "git";
        p.StartInfo.Arguments = args;
        p.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.UseShellExecute = false;
        p.Start();
        p.WaitForExit();
    }

    static string JsonOk(string data) => $"{{\"status\":\"ok\",\"data\":{ToJsonString(data)}}}";
    static string JsonOkRaw(string rawJson) => $"{{\"status\":\"ok\",\"data\":{rawJson}}}";
    static string JsonErr(string msg) => $"{{\"status\":\"error\",\"message\":{ToJsonString(msg)}}}";
    static string ToJsonString(string s) => $"\"{(s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    // TestRunner callbacks wrapper (full interface)
    class Callbacks : ICallbacks
    {
        public Action<ITestAdaptor> runStarted;
        public Action<ITestAdaptor> testStarted;
        public Action<ITestResultAdaptor> testFinished;
        public Action<ITestResultAdaptor> runFinished;

        public void RunStarted(ITestAdaptor testsToRun) => runStarted?.Invoke(testsToRun);
        public void TestStarted(ITestAdaptor test) => testStarted?.Invoke(test);
        public void TestFinished(ITestResultAdaptor r) => testFinished?.Invoke(r);
        public void RunFinished(ITestResultAdaptor r) => runFinished?.Invoke(r);
    }

    // JsonUtility can't serialize Dictionary directly
    [Serializable]
    class DictWrapper
    {
        public List<string> keys = new(); public List<string> values = new();
        public static DictWrapper From(Dictionary<string, string> d) { var w = new DictWrapper(); foreach (var kv in d) { w.keys.Add(kv.Key); w.values.Add(kv.Value); } return w; }
        public Dictionary<string, string> ToDict() { var d = new Dictionary<string, string>(); for (int i = 0; i < keys.Count; i++) d[keys[i]] = i < values.Count ? values[i] : ""; return d; }
    }
}

// Minimal JSON serializer for dictionaries/lists ¨ string (for console/tests/index payloads)
static class MiniJson
{
    public static string Serialize(object obj) => ToJson(obj);

    static string ToJson(object obj)
    {
        if (obj == null) return "null";
        if (obj is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        if (obj is System.Collections.IDictionary dict)
        {
            var parts = new List<string>();
            foreach (System.Collections.DictionaryEntry kv in dict)
                parts.Add(ToJson(kv.Key.ToString()) + ":" + ToJson(kv.Value));
            return "{" + string.Join(",", parts) + "}";
        }
        if (obj is System.Collections.IEnumerable list && !(obj is string))
        {
            var parts = new List<string>();
            foreach (var x in list) parts.Add(ToJson(x));
            return "[" + string.Join(",", parts) + "]";
        }
        if (obj is int or long or float or double or decimal)
            return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture);
        return ToJson(obj.ToString());
    }
}
#endif
