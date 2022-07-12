using System.Collections;
using System.Linq;
using System.Text;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class aiortcConnector : MonoBehaviour
{
    [SerializeField] private string aiortcServerURL;
    [SerializeField] private RawImage receiveVideo;
    [SerializeField] private VideoTransformType videoTransformType;

    public enum VideoTransformType
    {
        None,
        EdgeDetection,
        CartoonEffect,
        Rotate
    }

    private enum Side
    {
        Local,
        Remote
    }

    private class SignalingMsg
    {
        public string type;
        public string sdp;
        public string video_transform;
        public RTCSessionDescription ToDesc()
        {
            return new RTCSessionDescription
            {
                type = type == "offer" ? RTCSdpType.Offer : RTCSdpType.Answer,
                sdp = sdp
            };
        }
    }

    private RTCPeerConnection pc;
    private Camera cam;
    private RenderTexture rt;
    private MediaStream receiveVideoStream;

    void Start()
    {
        WebRTC.Initialize();
        StartCoroutine(WebRTC.Update());
        cam = Camera.main;
        rt = new RenderTexture(1920, 1080, 0, RenderTextureFormat.BGRA32, 0);
        Connect();
    }

    public void Connect()
    {
        pc = new RTCPeerConnection();
        pc.OnIceCandidate = cand =>
        {
            pc.OnIceCandidate = null;
            var msg = new SignalingMsg
            {
                type = pc.LocalDescription.type.ToString().ToLower(),
                sdp = pc.LocalDescription.sdp
            };

            switch (videoTransformType)
            {
                case VideoTransformType.None: msg.video_transform = "none"; break;
                case VideoTransformType.EdgeDetection: msg.video_transform = "edges"; break;
                case VideoTransformType.CartoonEffect: msg.video_transform = "cartoon"; break;
                case VideoTransformType.Rotate: msg.video_transform = "rotate"; break;
            }

            StartCoroutine(aiortcSignaling(msg));
        };
        pc.OnIceGatheringStateChange = state =>
        {
            Debug.Log($"OnIceGatheringStateChange > state: {state}");
        };
        pc.OnConnectionStateChange = state =>
        {
            Debug.Log($"OnConnectionStateChange > state: {state}");
        };
        pc.OnTrack = e =>
        {
            Debug.Log($"OnTrack");

            if (e.Track is VideoStreamTrack video)
            {
                Debug.Log($"OnTrackVideo");

                video.OnVideoReceived += tex =>
                {
                    receiveVideo.texture = tex;
                };
                receiveVideoStream = e.Streams.First();
                receiveVideoStream.OnRemoveTrack = ev =>
                {
                    receiveVideo.texture = null;
                    ev.Track.Dispose();
                };
            }
        };
        var videoTrack = new VideoStreamTrack(rt);
        pc.AddTrack(videoTrack);
        StartCoroutine(CreateDesc(RTCSdpType.Offer));
    }

    private IEnumerator CreateDesc(RTCSdpType type)
    {
        var op = type == RTCSdpType.Offer ? pc.CreateOffer() : pc.CreateAnswer();
        yield return op;

        if (op.IsError)
        {
            Debug.LogError($"Create {type} Error: {op.Error.message}");
            yield break;
        }

        StartCoroutine(SetDesc(Side.Local, op.Desc));
    }

    private IEnumerator SetDesc(Side side, RTCSessionDescription desc)
    {
        var op = side == Side.Local ? pc.SetLocalDescription(ref desc) : pc.SetRemoteDescription(ref desc);
        yield return op;

        if (op.IsError)
        {
            Debug.Log($"Set {desc.type} Error: {op.Error.message}");
            yield break;
        }

        if (side == Side.Local)
        {
            // aiortc not support Tricle ICE. 
        }
        else if (desc.type == RTCSdpType.Offer)
        {
            yield return StartCoroutine(CreateDesc(RTCSdpType.Answer));
        }
    }

    private IEnumerator aiortcSignaling(SignalingMsg msg)
    {
        var jsonStr = JsonUtility.ToJson(msg);
        using var req = new UnityWebRequest($"{aiortcServerURL}/{msg.type}", "POST");
        var bodyRaw = Encoding.UTF8.GetBytes(jsonStr);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        var resMsg = JsonUtility.FromJson<SignalingMsg>(req.downloadHandler.text);

        yield return StartCoroutine(SetDesc(Side.Remote, resMsg.ToDesc()));
    }

    void Update()
    {
        var originalTargetTexture = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = originalTargetTexture;
    }
}

