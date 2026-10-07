import json
import urllib.request

URL = "http://127.0.0.1:8080/mcp"

def post(payload, session_id=None):
    headers = {"Accept": "application/json, text/event-stream", "Content-Type": "application/json"}
    if session_id: headers["Mcp-Session-Id"] = session_id
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), headers=headers, method="POST")
    with urllib.request.urlopen(req, timeout=60) as response:
        return response.headers, response.read().decode()

def rpc(session_id, request_id, method, params):
    _, body = post({"jsonrpc":"2.0","id":request_id,"method":method,"params":params}, session_id)
    lines = [line[6:] for line in body.splitlines() if line.startswith("data: ")]
    return json.loads(lines[-1]) if lines else {"raw":body}

headers, _ = post({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"codex-inspector","version":"1.0"}}})
sid = headers.get("mcp-session-id")
post({"jsonrpc":"2.0","method":"notifications/initialized","params":{}}, sid)

for request_id, uri in enumerate(["mcpforunity://instances","mcpforunity://editor/state","mcpforunity://pipeline/renderer-features","mcpforunity://scene/volumes","mcpforunity://scene/cameras"], start=10):
    print("\nRESOURCE", uri)
    print(json.dumps(rpc(sid, request_id, "resources/read", {"uri":uri}), ensure_ascii=False))

code = r'''
var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
sb.AppendLine("SCENE=" + scene.path);
var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
sb.AppendLine("PIPELINE=" + (rp ? rp.GetType().FullName + "|" + rp.name : "BuiltIn"));
sb.AppendLine("SUN=" + (UnityEngine.RenderSettings.sun ? UnityEngine.RenderSettings.sun.name : "<none>"));
var lights = UnityEngine.Object.FindObjectsOfType<UnityEngine.Light>(true);
sb.AppendLine("LIGHT_COUNT=" + lights.Length);
foreach (var l in lights) {
  var add = l.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
  sb.Append("LIGHT|").Append(l.name).Append("|active=").Append(l.gameObject.activeInHierarchy && l.enabled).Append("|type=").Append(l.type).Append("|mode=").Append(l.lightmapBakeType).Append("|intensity=").Append(l.intensity).Append("|range=").Append(l.range).Append("|spot=").Append(l.spotAngle).Append("|shadows=").Append(l.shadows).Append("|strength=").Append(l.shadowStrength).Append("|bias=").Append(l.shadowBias).Append("|normalBias=").Append(l.shadowNormalBias).Append("|near=").Append(l.shadowNearPlane).Append("|culling=").Append(l.cullingMask).Append("|renderMode=").Append(l.renderMode);
  if (add) sb.Append("|urpShadows=").Append(add.renderShadows).Append("|lightLayers=").Append(add.lightLayerMask).Append("|softQuality=").Append(add.softShadowQuality);
  sb.AppendLine();
}
var renderers = UnityEngine.Object.FindObjectsOfType<UnityEngine.Renderer>(true);
int on=0,off=0,two=0,receive=0; var shaders=new System.Collections.Generic.Dictionary<string,int>();
foreach(var r in renderers){if(r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off)off++;else on++;if(r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.TwoSided)two++;if(r.receiveShadows)receive++;foreach(var m in r.sharedMaterials)if(m&&m.shader){var n=m.shader.name;if(!shaders.ContainsKey(n))shaders[n]=0;shaders[n]++;}}
sb.AppendLine("RENDERERS="+renderers.Length+"|castOn="+on+"|castOff="+off+"|twoSided="+two+"|receive="+receive);
foreach(var kv in shaders)sb.AppendLine("SHADER|"+kv.Key+"|slots="+kv.Value);
var urp=rp as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
if(urp)sb.AppendLine("URP|mainShadows="+urp.supportsMainLightShadows+"|additionalLights="+urp.additionalLightsRenderingMode+"|additionalShadows="+urp.supportsAdditionalLightShadows+"|soft="+urp.supportsSoftShadows+"|shadowDistance="+urp.shadowDistance+"|cascadeCount="+urp.shadowCascadeCount+"|mainAtlas="+urp.mainLightShadowmapResolution+"|additionalAtlas="+urp.additionalLightsShadowmapResolution+"|perObjectLimit="+urp.maxAdditionalLightsCount);
return sb.ToString();
'''
print("\nLIVE_SCENE_DIAGNOSTICS")
print(json.dumps(rpc(sid,30,"tools/call",{"name":"execute_code","arguments":{"action":"execute","code":code,"safety_checks":True,"compiler":"auto"}}),ensure_ascii=False))
print("\nCONSOLE_ERRORS")
print(json.dumps(rpc(sid,31,"tools/call",{"name":"read_console","arguments":{"action":"get","types":["error","warning"],"count":30,"format":"detailed","include_stacktrace":False}}),ensure_ascii=False))
