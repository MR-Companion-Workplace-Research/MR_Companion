# MR Companion

A Mixed Reality companion application for Meta Quest. An AI-powered avatar is spawned on the user's desk using MRUK scene understanding, capable of real-time voice conversation, gaze tracking, lip sync, and natural gestures.

## Features

- **Desk placement** — Detects the user's desk via MRUK and spawns the avatar on it, facing the user
- **Voice conversation** — Two backend options:
  - **ElevenLabs Conversational AI** — agent personality and voice configured via the ElevenLabs dashboard
  - **OpenAI Realtime API** — direct GPT voice interaction with server-side VAD
- **Gaze tracking** — Avatar head smoothly follows the user with configurable angle limits
- **Lip sync** — Mouth blendshape driven by real-time audio amplitude
- **Auto blink** — Randomized blink animation via blendshapes
- **Gestures** — Random upper-body gesture triggered each time the AI speaks
- **Experimenter remote control** — UDP commands from a PC to trigger conversation, mute mic, toggle gaze, etc.

## Requirements

- Unity 2022.3+ (built with URP)
- Meta XR SDK (MRUK, OVRCameraRig)
- Newtonsoft JSON (`com.unity.nuget.newtonsoft-json`)
- Meta Quest 3 / 3S with Scene Understanding enabled

## Project Structure

```
MR_Companion/
├── Assets/
│   ├── Scripts/          # Core scripts (see Scripts/README.md for details)
│   ├── Animations/       # Idle, sitting, and gesture animations
│   ├── Animator/         # Animator controllers (human-sized / miniature)
│   ├── Oculus/           # Meta XR configuration
│   ├── Prefabs/          # Avatar and scene prefabs
│   ├── Scenes/           # Unity scenes
│   └── Rukha93/          # Modular Anime Character asset
├── Experimenter_side_script/
│   ├── experimenter_control_EN.py   # English version of experimenter CLI (still under construct)
│   └── experimenter_control_CH.py   # Chinese version of experimenter CLI (still under construct)
├── Packages/
└── ProjectSettings/
```

## Setup

1. Open the project in Unity
2. Set the build target to **Android** (Meta Quest)
3. Configure your API key:
   - **ElevenLabs**: set the Agent ID on the `ElevenLabsConnection` component
   - **OpenAI**: set the API key on the `RealtimeAPIConnection` component
4. Ensure the Quest has completed **Space Setup** with a desk/table scanned
5. Build and deploy to the Quest

## Experimenter Remote Control

The `ExperimenterRemoteTrigger` script listens on UDP port `9100`. The Quest's IP is logged to the console on startup. Both devices must be on the same network.

| Command | Description |
|---|---|
| `INITIATE` | Trigger companion to start talking |
| `CONTEXT:text` | Silent contextual update |
| `MIC:ON` / `MIC:OFF` | Mute/unmute user mic |
| `GAZE:ON` / `GAZE:OFF` | Toggle gaze tracking |
| `END_CONVERSATION` | AI wraps up and says goodbye |
| `STATUS` | Query current state |
