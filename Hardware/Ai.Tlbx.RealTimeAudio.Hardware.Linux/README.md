# Linux Audio Hardware for Ai.Tlbx.RealTimeAudio

This package provides Linux support for the Ai.Tlbx.RealTimeAudio library by implementing the `IAudioHardwareAccess` interface.

## Requirements

- .NET 9.0 or later
- Linux operating system with ALSA support
- ALSA development libraries (`libasound2` package on Debian/Ubuntu-based distributions)

## Installation

To use this package, install the ALSA libraries on your Linux system:

```bash
# Debian/Ubuntu
sudo apt install libasound2

# Fedora
sudo dnf install alsa-lib

# Arch Linux
sudo pacman -S alsa-lib
```

## Features

- Audio input from the default or selected microphone
- Audio output to the default or selected speakers
- Device enumeration for available audio devices
- PCM16 audio format compatibility with OpenAI's API 