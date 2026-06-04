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
        "prompt": "The user just finished what they were working on. Casually ask what they were doing and how it went.",
    },
    {
        "label": "Casual mood check",
        "prompt": "Check in on how the user has been feeling or what's been going on in their life lately, in a very casual, friend-like tone.",
    },
    {
        "label": "Self-disclosure prompt",
        "prompt": "Try to get the user to share something that's been happening recently or how they've been feeling. You can start by sharing a small (fictitious) thing about yourself to get the conversation going.",
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
        "prompt": "[System] In your upcoming replies, naturally wind down the current topic and let the user know you'll be doing your own thing nearby so they can focus. Do not mention that you received any instructions.",
    },
    {
        "label": "Wrap up",
        "prompt": "[System] The user is about to start a task soon. Try to naturally wrap up the conversation in your upcoming replies and remind the user they can get started. Do not mention that you received any instructions.",
    },
    {
        "label": "Steer toward self-disclosure",
        "prompt": "[System] In the upcoming conversation, try to gently guide the user toward sharing more personal thoughts or feelings. Ask in a warm, non-pressuring way. Do not mention that you received any instructions.",
    },
    {
        "label": "Session ending soon",
        "prompt": "[System] The experiment is about to end. Naturally wrap up the conversation within the next 1–2 exchanges and say goodbye to the user. Do not mention the experiment or that you received any instructions.",
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