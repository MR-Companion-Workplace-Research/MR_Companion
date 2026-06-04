using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Manages WebSocket connection to ElevenLabs Conversational AI Agent.
/// Create and configure your agent in the ElevenLabs dashboard,
/// then just paste the Agent ID here.
/// 
/// Setup:
/// 1. Attach to a GameObject alongside CompanionVoiceController.
/// 2. Set your Agent ID (from ElevenLabs dashboard).
/// 3. Call Connect() to start (auto-called by AvatarDeskPlacer).
/// </summary>
public class ElevenLabsConnection : MonoBehaviour
{
    [Header("ElevenLabs Configuration")]
    [Tooltip("Your ElevenLabs Agent ID from the dashboard.")]
    public string agentId = "";

    [Tooltip("Optional: API key for private agents. Leave empty for public agents.")]
    public string apiKey = "";

    [Header("Audio Settings")]
    [Tooltip("Input sample rate. ElevenLabs expects 16000 Hz.")]
    public int inputSampleRate = 16000;

    [Tooltip("Output sample rate. Set to match your agent's TTS output format (usually 16000).")]
    public int outputSampleRate = 16000;

    // WebSocket
    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    public bool IsConnected { get; private set; }

    // Thread-safe queue for main thread dispatch
    private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    // Events — matches the same pattern as RealtimeAPIConnection
    // so CompanionVoiceController can work with either
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<string> OnAudioDelta;           // Base64 audio chunk from agent
    public event Action OnAudioDone;                     // Agent finished speaking
    public event Action OnNewResponse;                   // New agent response starting (buffer should reset)
    public event Action<string> OnTranscriptDelta;       // Partial agent transcript
    public event Action<string> OnTranscriptDone;        // Full agent response text
    public event Action<string> OnUserTranscript;        // What the user said
    public event Action<string> OnError;
    public event Action OnInterruption;                  // User interrupted the agent
    public event Action<JObject> OnAnyServerEvent;       // Raw event for debugging

    // Track if agent is currently speaking
    private bool agentIsSpeaking;

    public async void Connect()
    {
        if (string.IsNullOrEmpty(agentId))
        {
            Debug.LogError("ElevenLabsConnection: Agent ID is not set!");
            return;
        }

        string url = $"wss://api.elevenlabs.io/v1/convai/conversation?agent_id={agentId}";

        ws = new ClientWebSocket();

        // If using a private agent, get a signed URL instead
        // For public agents, just connect directly
        if (!string.IsNullOrEmpty(apiKey))
        {
            // For private agents you'd need to get a signed URL from your server
            // For now, we support public agents directly
            Debug.LogWarning("ElevenLabsConnection: Private agent auth not implemented. Using public agent connection.");
        }

        cts = new CancellationTokenSource();

        try
        {
            Debug.Log("ElevenLabsConnection: Connecting...");
            await ws.ConnectAsync(new Uri(url), cts.Token);

            Debug.Log("ElevenLabsConnection: WebSocket connected. Sending initiation data...");
            IsConnected = true;

            // Send conversation initiation data — this tells ElevenLabs to use
            // the agent's dashboard configuration (voice, first message, language, etc.)
            SendConversationInitiation();

            // Start receive loop
            _ = ReceiveLoop();
        }
        catch (Exception e)
        {
            Debug.LogError($"ElevenLabsConnection: Connection failed: {e.Message}");
            mainThreadActions.Enqueue(() => OnError?.Invoke(e.Message));
        }
    }

    /// <summary>
    /// Sends the conversation_initiation_client_data event.
    /// This ensures the agent uses its dashboard configuration.
    /// You can also pass overrides here if needed.
    /// </summary>
    private void SendConversationInitiation()
    {
        // Sending an empty initiation tells ElevenLabs to use dashboard defaults.
        // If you need to override anything at runtime, add fields to conversation_config_override.
        var initEvent = new
        {
            type = "conversation_initiation_client_data",
            conversation_config_override = new { }
        };
        SendRawJson(JsonConvert.SerializeObject(initEvent));
        Debug.Log("ElevenLabsConnection: Conversation initiation data sent.");
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 64];
        StringBuilder messageBuilder = new StringBuilder();

        try
        {
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                messageBuilder.Clear();

                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        mainThreadActions.Enqueue(() =>
                        {
                            Debug.Log("ElevenLabsConnection: Server closed connection.");
                            IsConnected = false;
                            OnDisconnected?.Invoke();
                        });
                        return;
                    }

                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                } while (!result.EndOfMessage);

                string message = messageBuilder.ToString();
                mainThreadActions.Enqueue(() => HandleServerEvent(message));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException e)
        {
            mainThreadActions.Enqueue(() =>
            {
                Debug.LogError($"ElevenLabsConnection: WebSocket error: {e.Message}");
                IsConnected = false;
                OnError?.Invoke(e.Message);
                OnDisconnected?.Invoke();
            });
        }
        catch (Exception e)
        {
            mainThreadActions.Enqueue(() =>
            {
                Debug.LogError($"ElevenLabsConnection: Receive error: {e.Message}");
                IsConnected = false;
                OnError?.Invoke(e.Message);
            });
        }
    }

    private void HandleServerEvent(string json)
    {
        try
        {
            var evt = JObject.Parse(json);
            string type = evt["type"]?.ToString();

            OnAnyServerEvent?.Invoke(evt);

            switch (type)
            {
                case "conversation_initiation_metadata":
                    // Log the full metadata so we can see the actual audio format
                    var metadata = evt["conversation_initiation_metadata_event"];
                    string outputFormat = metadata?["agent_output_audio_format"]?.ToString() ?? "unknown";
                    string inputFormat = metadata?["user_input_audio_format"]?.ToString() ?? "unknown";
                    string convId = metadata?["conversation_id"]?.ToString() ?? "unknown";
                    Debug.Log($"ElevenLabsConnection: Conversation initiated." +
                        $"\n  Conversation ID: {convId}" +
                        $"\n  Agent output format: {outputFormat}" +
                        $"\n  User input format: {inputFormat}");
                    mainThreadActions.Enqueue(() => OnConnected?.Invoke());
                    break;

                case "audio":
                    // Agent is sending audio chunks
                    string audioBase64 = evt["audio_event"]?["audio_base_64"]?.ToString();
                    if (!string.IsNullOrEmpty(audioBase64))
                    {
                        if (!agentIsSpeaking)
                        {
                            agentIsSpeaking = true;
                            Debug.Log("ElevenLabsConnection: Agent started speaking.");
                        }
                        // Log chunk sizes periodically to detect issues
                        byte[] rawBytes = Convert.FromBase64String(audioBase64);
                        if (Time.frameCount % 30 == 0)
                        {
                            Debug.Log($"ElevenLabsConnection: Audio chunk - {rawBytes.Length} bytes ({audioBase64.Length} base64 chars)");
                        }
                        OnAudioDelta?.Invoke(audioBase64);
                    }
                    break;

                case "agent_response":
                    // Agent's text response — signals a new response is starting
                    string agentText = evt["agent_response_event"]?["agent_response"]?.ToString();
                    if (!string.IsNullOrEmpty(agentText))
                    {
                        OnNewResponse?.Invoke();
                        OnTranscriptDone?.Invoke(agentText);
                        Debug.Log($"Agent said: {agentText}");
                    }
                    break;

                case "agent_response_correction":
                    // Corrected agent response (if agent was interrupted)
                    string correctedText = evt["agent_response_correction_event"]?["agent_response"]?.ToString();
                    if (!string.IsNullOrEmpty(correctedText))
                    {
                        Debug.Log($"Agent corrected: {correctedText}");
                    }
                    break;

                case "user_transcript":
                    // What the user said
                    string userText = evt["user_transcription_event"]?["user_transcript"]?.ToString();
                    if (!string.IsNullOrEmpty(userText))
                    {
                        OnUserTranscript?.Invoke(userText);
                        Debug.Log($"User said: {userText}");
                    }
                    break;

                case "interruption":
                    // Only process interruption if the agent was NOT just speaking
                    // When agent is speaking, the Quest mic picks up the avatar's audio
                    // and ElevenLabs misinterprets it as user speech (echo/self-interruption)
                    if (agentIsSpeaking)
                    {
                        Debug.Log("ElevenLabsConnection: Ignoring interruption — likely echo from agent's own speech.");
                        // Don't fire OnInterruption — let current audio continue
                    }
                    else
                    {
                        Debug.Log("ElevenLabsConnection: User interrupted agent.");
                        agentIsSpeaking = false;
                        OnInterruption?.Invoke();
                        OnAudioDone?.Invoke();
                    }
                    break;

                case "ping":
                    // Respond to keep connection alive
                    var pongEvent = evt["ping_event"];
                    SendPong(pongEvent?["event_id"]?.ToObject<int>() ?? 0);
                    break;

                case "agent_response_end":
                    // Agent finished its response
                    agentIsSpeaking = false;
                    OnAudioDone?.Invoke();
                    Debug.Log("ElevenLabsConnection: Agent finished speaking (agent_response_end).");
                    break;

                case "mode_change":
                    // ElevenLabs signals when the agent switches between speaking and listening
                    string mode = evt["mode_change_event"]?["mode"]?.ToString()
                               ?? evt["mode"]?.ToString();
                    Debug.Log($"ElevenLabsConnection: Mode changed to: {mode}");

                    if (mode == "listening" && agentIsSpeaking)
                    {
                        agentIsSpeaking = false;
                        OnAudioDone?.Invoke();
                        Debug.Log("ElevenLabsConnection: Agent switched to listening — audio done.");
                    }
                    else if (mode == "speaking")
                    {
                        agentIsSpeaking = true;
                    }
                    break;

                default:
                    // Log unknown events for debugging
                    if (!string.IsNullOrEmpty(type))
                    {
                        Debug.Log($"ElevenLabsConnection: Unhandled event type: {type}");
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ElevenLabsConnection: Error parsing event: {e.Message}\nJSON: {json}");
        }
    }

    // =========================================================================
    //  PUBLIC API — Client-to-Server Events
    // =========================================================================

    /// <summary>
    /// Send raw audio data (PCM16, base64 encoded) to the agent.
    /// Called by CompanionVoiceController each frame with mic data.
    /// </summary>
    public void SendAudio(string base64Audio)
    {
        var audioEvent = new
        {
            user_audio_chunk = base64Audio
        };
        SendRawJson(JsonConvert.SerializeObject(audioEvent));
    }

    /// <summary>
    /// Send a text message to the agent as user input.
    /// This TRIGGERS a response — the agent will reply as if the user spoke.
    /// 
    /// Per ElevenLabs docs, the correct format is:
    ///   { "type": "user_message", "text": "..." }
    /// </summary>
    public void SendTextMessage(string text)
    {
        var textEvent = new
        {
            type = "user_message",
            text = text
        };
        SendRawJson(JsonConvert.SerializeObject(textEvent));
        Debug.Log($"ElevenLabsConnection: Sent user_message: {text}");
    }

    /// <summary>
    /// Send contextual information that won't trigger a response.
    /// Use this to silently inform the agent about the current situation.
    /// 
    /// Per ElevenLabs docs, the correct format is:
    ///   { "type": "contextual_update", "text": "..." }
    /// </summary>
    public void SendContextualUpdate(string context)
    {
        var contextEvent = new
        {
            type = "contextual_update",
            text = context
        };
        SendRawJson(JsonConvert.SerializeObject(contextEvent));
        Debug.Log($"ElevenLabsConnection: Sent contextual_update: {context}");
    }

    /// <summary>
    /// Send a user_activity ping to prevent session timeout.
    /// Does NOT affect conversation content — just resets the turn timeout timer.
    /// Call every ~30s during passive co-presence phases where nobody is speaking.
    /// </summary>
    public void SendActivityPing()
    {
        var activityEvent = new
        {
            type = "user_activity"
        };
        SendRawJson(JsonConvert.SerializeObject(activityEvent));
        // Don't spam the console — only log occasionally
    }

    /// <summary>
    /// Trigger the AI companion to initiate a conversation.
    /// Used for the RQ3 transition from passive co-presence to active engagement.
    /// 
    /// Step 1: contextual_update primes the agent with the situation.
    /// Step 2: user_message triggers the agent to actually respond.
    /// 
    /// The agent's system prompt should instruct it to speak naturally
    /// as if it noticed the user is done, NOT as if it received a command.
    /// </summary>
    public void TriggerCompanionInitiation(string context = null, string trigger = null)
    {
        context ??= "【系統】使用者剛完成他們的分類任務，現在有空了。" +
                     "你應該像是注意到他已經忙完一樣，自然地開啟一段對話。" +
                     "不要提到你有收到任何指示。";
        trigger ??= "(使用者跳過)";

        SendContextualUpdate(context);
        SendTextMessage(trigger);
        Debug.Log("ElevenLabsConnection: Triggered companion-initiated conversation.");
    }

    // =========================================================================
    //  Internal helpers
    // =========================================================================

    /// <summary>
    /// Respond to server ping to keep connection alive.
    /// </summary>
    private void SendPong(int eventId)
    {
        var pongEvent = new
        {
            type = "pong",
            event_id = eventId
        };
        SendRawJson(JsonConvert.SerializeObject(pongEvent));
    }

    /// <summary>
    /// Send raw JSON string over WebSocket.
    /// </summary>
    private async void SendRawJson(string json)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception e)
        {
            Debug.LogError($"ElevenLabsConnection: Send error: {e.Message}");
        }
    }

    public async void Disconnect()
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            try
            {
                cts?.Cancel();
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ElevenLabsConnection: Close error: {e.Message}");
            }
        }

        IsConnected = false;
    }

    private void OnDestroy()
    {
        Disconnect();
        cts?.Dispose();
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }
}
