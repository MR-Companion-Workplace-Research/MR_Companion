#!/usr/bin/env python3
"""
Experimenter Remote Control for MR Companion Study
===================================================
Run this on the experimenter's laptop (same WiFi as Quest 3).

Usage:
    python experimenter_control.py <quest_ip>

Commands (type these in the terminal):
    i  or  initiate     — Trigger companion to start talking (pick a preset or custom)
    c  or  context      — Send a silent context update (pick a preset or custom)
    p  or  ping         — Send activity ping to keep session alive
    e  or  end          — Tell AI to wrap up the conversation
    s  or  status       — Check Quest app connection status

    m  or  mic          — Toggle mic mute/unmute
    m on / m off        — Explicit mic on/off
    g on / g off        — Enable/disable avatar gaze + head tracking

    q  or  quit         — Exit this script
"""

import socket
import sys

# =============================================================================
#  CONFIGURATION — Edit these presets freely, no Unity recompile needed!
# =============================================================================

QUEST_PORT = 9100       # Must match ExperimenterRemoteTrigger.listenPort
RECV_TIMEOUT = 2.0      # Seconds to wait for reply

# ── INITIATE presets ─────────────────────────────────────────────────────────
# Each entry: { "label": "...", "prompt": "..." }
# "prompt" is sent as INITIATE:<prompt>. Empty string = default (no custom prompt).
INITIATE_PRESETS = [
    {
        "label": "Default greeting (no custom prompt)",
        "prompt": "",
    },
    {
        "label": "Post-task check-in",
        "prompt": "使用者剛做完他要忙的事情。用輕鬆的方式問他剛剛在做什麼、做得怎麼樣。",
    },
    {
        "label": "Casual mood check",
        "prompt": "用很隨意的語氣關心使用者最近的心情或生活狀況，像朋友聊天一樣。",
    },
    {
        "label": "Self-disclosure prompt",
        "prompt": "試著讓使用者分享一些最近發生的事情或心情。可以先分享一點自己的（虛構的）小事來帶動話題。",
    },
    # Add more presets here as needed:
    # {
    #     "label": "Your label",
    #     "prompt": "Your prompt text",
    # },
]

# ── CONTEXT presets ──────────────────────────────────────────────────────────
# Sent as CONTEXT:<text>. Silent — AI adjusts behavior without responding directly.
CONTEXT_PRESETS = [
    {
        "label": "Transition",
        "prompt": "【系統】請在接下來的回覆中自然地結束目前的話題，告訴使用者你會在旁邊做自己的事，讓他先忙。不要提到你有收到任何指示。",
    },
    {
        "label": "Wrap up",
        "prompt": "【系統】使用者待會要開始進行任務了。請試著在接下來的回覆中自然地結束話題，提醒使用者可以開始做事了。不要提到你有收到任何指示。",
    },
    {
        "label": "Steer toward self-disclosure",
        "prompt": "【系統】在接下來的對話中，試著引導使用者分享更多個人的想法或感受。用溫和、不強迫的方式提問。不要提到你有收到任何指示。",
    },
    {
        "label": "Session ending soon",
        "prompt": "【系統】實驗即將結束。請在接下來1-2輪對話中自然地收尾，跟使用者道別。不要提到實驗或你有收到任何指示。",
    },
    # Add more presets here as needed:
    # {
    #     "label": "Your label",
    #     "prompt": "Your context text",
    # },
]

# ── END_CONVERSATION default context ────────────────────────────────────────
END_CONVERSATION_CONTEXT = (
    "The experiment session is ending. "
    "Wrap up the conversation naturally and say goodbye."
)

# =============================================================================
#  END OF CONFIGURATION
# =============================================================================


def send_command(sock: socket.socket, quest_ip: str, command: str) -> str | None:
    """Send a UDP command and wait for a reply."""
    try:
        sock.sendto(command.encode("utf-8"), (quest_ip, QUEST_PORT))
        sock.settimeout(RECV_TIMEOUT)
        data, _ = sock.recvfrom(1024)
        return data.decode("utf-8")
    except socket.timeout:
        return "(no reply — Quest may not have received the command)"
    except Exception as e:
        return f"(error: {e})"


def pick_preset(presets: list[dict], label: str) -> str | None:
    """
    Show numbered preset menu.
    Returns the chosen prompt string, or None if cancelled.
    """
    print(f"\n   ── {label} Presets ──")
    for i, p in enumerate(presets):
        print(f"   [{i}] {p['label']}")
    print(f"   [c] Custom input")
    print(f"   [x] Cancel")

    choice = input("   Pick: ").strip().lower()

    if choice == "x":
        return None
    elif choice == "c":
        custom = input("   Custom text: ").strip()
        return custom if custom else None
    else:
        try:
            idx = int(choice)
            if 0 <= idx < len(presets):
                selected = presets[idx]
                print(f"   → Using: {selected['label']}")
                return selected["prompt"]
            else:
                print("   Invalid index.")
                return None
        except ValueError:
            print("   Invalid input.")
            return None


def print_help():
    print()
    print("Commands:")
    print("  [i] initiate      — Companion starts talking (preset menu)")
    print("  [c] context       — Send silent context update (preset menu)")
    print("  [p] ping          — Activity ping (keep-alive)")
    print("  [e] end           — AI wraps up conversation")
    print("  [s] status        — Check connection status")
    print()
    print("  [m]     mic       — Toggle mic mute/unmute")
    print("  [m on]  mic on    — Unmute user mic")
    print("  [m off] mic off   — Mute user mic")
    print("  [g on]  gaze on   — Enable gaze + head tracking")
    print("  [g off] gaze off  — Disable gaze + head tracking")
    print()
    print("  [h] help          — Show this help")
    print("  [q] quit          — Exit")
    print()


def main():
    if len(sys.argv) < 2:
        print("Usage: python experimenter_control.py <quest_ip>")
        print("  The Quest IP is printed in the Unity console when the app starts.")
        sys.exit(1)

    quest_ip = sys.argv[1]
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    print("=" * 55)
    print("  MR Companion Study — Experimenter Remote Control")
    print(f"  Target: {quest_ip}:{QUEST_PORT}")
    print(f"  Presets: {len(INITIATE_PRESETS)} initiate, {len(CONTEXT_PRESETS)} context")
    print("=" * 55)
    print_help()

    while True:
        try:
            user_input = input(">> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\nExiting.")
            break

        if not user_input:
            continue

        parts = user_input.lower().split(None, 1)
        cmd = parts[0]
        arg = parts[1] if len(parts) > 1 else ""

        # ── Conversation commands ──

        if cmd in ("i", "initiate"):
            prompt = pick_preset(INITIATE_PRESETS, "INITIATE")
            if prompt is None:
                print("   Cancelled.")
                continue
            udp_cmd = f"INITIATE:{prompt}" if prompt else "INITIATE"
            reply = send_command(sock, quest_ip, udp_cmd)
            print(f"   -> {reply}")

        elif cmd in ("c", "context"):
            prompt = pick_preset(CONTEXT_PRESETS, "CONTEXT")
            if prompt is None:
                print("   Cancelled.")
                continue
            if not prompt:
                print("   Empty context, cancelled.")
                continue
            reply = send_command(sock, quest_ip, f"CONTEXT:{prompt}")
            print(f"   -> {reply}")

        elif cmd in ("p", "ping"):
            reply = send_command(sock, quest_ip, "PING")
            print(f"   -> {reply}")

        elif cmd in ("e", "end"):
            print(f"   Sending end conversation...")
            reply = send_command(sock, quest_ip, f"END_CONVERSATION:{END_CONVERSATION_CONTEXT}")
            print(f"   -> {reply}")

        elif cmd in ("s", "status"):
            reply = send_command(sock, quest_ip, "STATUS")
            print(f"   -> {reply}")

        # ── Mic commands ──

        elif cmd in ("m", "mic"):
            if arg in ("on", "off"):
                reply = send_command(sock, quest_ip, f"MIC:{arg.upper()}")
            else:
                reply = send_command(sock, quest_ip, "MIC")
            print(f"   -> {reply}")

        # ── Gaze commands ──

        elif cmd in ("g", "gaze"):
            if arg in ("on", "off"):
                reply = send_command(sock, quest_ip, f"GAZE:{arg.upper()}")
            else:
                print("   Usage: g on / g off")
                continue
            print(f"   -> {reply}")

        # ── Help & Quit ──

        elif cmd in ("h", "help"):
            print_help()

        elif cmd in ("q", "quit", "exit"):
            print("Exiting.")
            break

        else:
            print(f"   Unknown command: '{user_input}'. Type 'h' for help.")

    sock.close()


if __name__ == "__main__":
    main()