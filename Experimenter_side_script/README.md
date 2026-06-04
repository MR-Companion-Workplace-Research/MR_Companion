# Experimenter Remote Control

CLI tool for remotely controlling the MR Companion during study sessions. Sends UDP commands from the experimenter's laptop to the Quest headset running the companion app.

Two versions are available:
- `experimenter_control_EN.py` — English prompts
- `experimenter_control_CH.py` — Chinese (Traditional) prompts

Both versions are functionally identical; only the preset prompt text differs.

## Prerequisites

- Python 3.10+
- No external packages required (uses only `socket` and `sys` from the standard library)
- The experimenter's laptop and the Quest must be on the **same WiFi network**

## Finding the Quest IP

When the MR Companion app launches on the Quest, it logs the device IP to the Unity console:

```
=== EXPERIMENTER REMOTE TRIGGER ===
    Quest IP: 192.168.x.x
    Port:     9100
===================================
```

You can view this via **Meta Quest Developer Hub (MQDH)** > Logcat, or `adb logcat`.
Or you can just simply open on Meta Quest: Settings -> Wifi -> Details, and you can check the IP there (I usually just do this, it is simpler)

## Usage

```bash
python experimenter_control_EN.py <quest_ip>
```

Example:

```bash
python experimenter_control_EN.py 192.168.137.32
```

Once running, you'll see an interactive prompt (`>>`). Type a command and press Enter.

## Commands

### Conversation Control

| Command | Shortcut | Description |
|---|---|---|
| `initiate` | `i` | Trigger the companion to start talking. Opens a preset menu where you can pick a predefined prompt or type a custom one. |
| `context` | `c` | Send a silent context update to the AI agent. The agent adjusts its behavior without responding directly. Opens a preset menu. |
| `ping` | `p` | Send an activity ping to keep the ElevenLabs session alive. |
| `end` | `e` | Tell the AI to naturally wrap up the conversation and say goodbye. |
| `status` | `s` | Query the Quest for current connection state and mic status. |

### Avatar Control

| Command | Shortcut | Description |
|---|---|---|
| `mic on` | `m on` | Unmute the user's microphone. |
| `mic off` | `m off` | Mute the user's microphone (mic hardware stays active, audio is just not sent to the API). |
| `mic` | `m` | Toggle mic mute/unmute. |
| `gaze on` | `g on` | Enable avatar gaze and head tracking (avatar looks at the user). (under construct)|
| `gaze off` | `g off` | Disable gaze tracking (head returns to animation control). (under construct)|

### Other

| Command | Shortcut | Description |
|---|---|---|
| `help` | `h` | Show command help in the terminal. |
| `quit` | `q` | Exit the script. |

## Preset Menus

When you type `i` (initiate) or `c` (context), a numbered menu appears with predefined prompts:

```
   ── INITIATE Presets ──
   [0] Default greeting (no custom prompt)
   [1] Post-task check-in
   [2] Casual mood check
   [3] Self-disclosure prompt
   [c] Custom input
   [x] Cancel
   Pick:
```

- Type a number to pick a preset
- Type `c` to enter a custom prompt
- Type `x` to cancel

### Customizing Presets

Edit the `INITIATE_PRESETS` and `CONTEXT_PRESETS` lists at the top of the script. Each entry has a `label` (shown in the menu) and a `prompt` (sent to the agent):

```python
INITIATE_PRESETS = [
    {
        "label": "Your label here",
        "prompt": "Your prompt text here",
    },
]
```

No Unity recompile is needed — presets are purely on the experimenter side.

## Typical Study Flow

1. Start the MR Companion app on the Quest
2. Note the Quest IP from the console log
3. Run the experimenter script on your laptop
4. Use `s` (status) to verify the connection
5. Use `i` (initiate) to trigger the companion when needed
6. Use `c` (context) to silently steer the conversation
7. Use `e` (end) to wrap up the session

## Troubleshooting

| Issue | Solution |
|---|---|
| `(no reply)` after every command | Check that both devices are on the same network. Verify the Quest IP. Make sure the app is running. |
| Mic echo / self-interruption | The script includes `MIC:OFF` / `MIC:ON` — mute the mic while the AI is speaking if echo is a problem. |
| Session timeout | Use `p` (ping) manually, or rely on the auto-ping built into the Quest app (every 25s by default). |
| Wrong port | Ensure `QUEST_PORT` in the script (default `9100`) matches `ExperimenterRemoteTrigger.listenPort` in Unity. |
