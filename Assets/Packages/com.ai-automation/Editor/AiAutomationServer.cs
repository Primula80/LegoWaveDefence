// Assets/Packages/com.yourco.ai-automation/Editor/AiAutomationServer.cs
#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AiAutomationServer
{
    // CONFIG
    private const string k_Prefix = "http://127.0.0.1:17890/";
    private const string k_HeaderToken = "X-AI-Token";
    private static readonly string k_Token =
        Environment.GetEnvironmentVariable("AI_AUTOMATION_TOKEN") ?? "CHANGE_ME";

    static HttpListener _listener;
    static Thread _thread;

    static AiAutomationServer()
    {
        // Delay start so Editor is fully up
        EditorApplication.delayCall += Start;
        EditorApplication.quitting += Stop;
    }

    [MenuItem("AI Automation/Toggle Server")]
    static void Toggle()
    {
        if (_listener == null) Start();
        else Stop();
    }

    static void Start()
    {
        Debug.Log("AI_AUTOMATION_TOKEN=" + Environment.GetEnvironmentVariable("AI_AUTOMATION_TOKEN"));


        try
        {
            if (_listener != null) return;
            _listener = new HttpListener();
            _listener.Prefixes.Add(k_Prefix);
            _listener.Start();

            _thread = new Thread(Loop) { IsBackground = true };
            _thread.Start();
            Debug.Log($"[AI Server] Listening at {k_Prefix}");
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
                // Marshal to main thread for all Unity API calls
                EditorApplication.delayCall += () => Handle(ctx);
            }
            catch { /* listener closed */ }
        }
    }

    static void Handle(HttpListenerContext ctx)
    {
        try
        {
            // Auth
            if (ctx.Request.Headers[k_HeaderToken] != k_Token)
            {
                Write(ctx, 401, JsonErr("unauthorized"));
                return;
            }

            var route = ctx.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();
            string body = "";
            using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                body = reader.ReadToEnd();

            string res = route switch
            {
                "ping" => JsonOk("pong"),
                "open_scene" => OpenScene(body),
                "save_scene" => SaveScene(),
                "create_game_object" => CreateGameObject(body),
                "add_component" => AddComponent(body),
                "set_property" => SetProperty(body),
                "set_transform" => SetTransform(body),
                "create_prefab" => CreatePrefab(body),
                "link_reference" => LinkReference(body),
                "execute_menu_item" => ExecuteMenuItem(body),
                _ => JsonErr($"unknown route: {route}")
            };

            Write(ctx, 200, res);
            TryAutoCommit($"AI:{route}");
        }
        catch (Exception e)
        {
            Write(ctx, 500, JsonErr(e.ToString()));
        }
    }

    // ---- Endpoints ----

    // { "path":"Assets/_Game/Scenes/Proto.unity" }
    static string OpenScene(string json)
    {
        var req = JsonUtility.FromJson<OpenSceneReq>(json);
        if (!File.Exists(req.path)) return JsonErr($"scene not found: {req.path}");
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

    // { "name":"Player", "parent":"/Root" }
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

    // { "path":"/Player", "type":"UnityEngine.CharacterController, UnityEngine.PhysicsModule" }
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

    // { "path":"/Player", "position":[0,1,0], "rotation":[0,0,0], "scale":[1,1,1] }
    static string SetTransform(string json)
    {
        var req = JsonUtility.FromJson<SetTransformReq>(json);
        var go = GameObject.Find(req.path);
        if (!go) return JsonErr($"gameobject not found: {req.path}");
        if (req.position != null && req.position.Length == 3)
            go.transform.position = new Vector3(req.position[0], req.position[1], req.position[2]);
        if (req.rotation != null && req.rotation.Length == 3)
            go.transform.eulerAngles = new Vector3(req.rotation[0], req.rotation[1], req.rotation[2]);
        if (req.scale != null && req.scale.Length == 3)
            go.transform.localScale = new Vector3(req.scale[0], req.scale[1], req.scale[2]);
        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return JsonOk("ok");
    }

    // { "from":"/Enemy", "to":"Assets/_Game/Prefabs/Enemy.prefab" }
    static string CreatePrefab(string json)
    {
        var req = JsonUtility.FromJson<CreatePrefabReq>(json);
        var go = GameObject.Find(req.from);
        if (!go) return JsonErr($"gameobject not found: {req.from}");
        var dir = Path.GetDirectoryName(req.to);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, req.to);
        AssetDatabase.SaveAssets();
        return JsonOk(AssetDatabase.AssetPathToGUID(req.to));
    }

    // { "path":"/Player", "component":"YourNamespace.PlayerController, Assembly-CSharp", "property":"speed", "value":"7.5" }
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

    // { "target":"/Gun", "component":"YourNamespace.Inventory, Assembly-CSharp", "property":"equippedItem", "source":"/GunPrefab" }
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

    // { "menu":"File/Save" }
    static string ExecuteMenuItem(string json)
    {
        var req = JsonUtility.FromJson<MenuReq>(json);
        var ok = EditorApplication.ExecuteMenuItem(req.menu);
        return ok ? JsonOk("ok") : JsonErr($"menu failed: {req.menu}");
    }

    // ---- utils ----

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
        try
        {
            RunGit("add -A");
            RunGit($"commit -m \"{message}\"");
        }
        catch { /* optional */ }
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
    static string JsonErr(string msg) => $"{{\"status\":\"error\",\"message\":{ToJsonString(msg)}}}";
    static string ToJsonString(string s) => $"\"{s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? ""}\"";

    // request DTOs
    [Serializable] class OpenSceneReq { public string path; }
    [Serializable] class CreateGoReq { public string name; public string parent; }
    [Serializable] class AddCompReq { public string path; public string type; }
    [Serializable] class SetTransformReq { public string path; public float[] position; public float[] rotation; public float[] scale; }
    [Serializable] class CreatePrefabReq { public string from; public string to; }
    [Serializable] class SetPropReq { public string path; public string component; public string property; public string value; }
    [Serializable] class LinkRefReq { public string target; public string component; public string property; public string source; }
    [Serializable] class MenuReq { public string menu; }
}
#endif
