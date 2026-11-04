# Quick start

[Español](INICIO_RAPIDO.md) · **English**

[Download](https://github.com/conr4d86/Catalyst-CF-Editor/releases) ·
[FAQ](FAQ.md#english) · [Recovery](RECUPERACION.md#english)

## 1. Open the app and connect the amplifier

Download **CatalystCFEditor.exe** and run it on Windows. The application,
Custom Firmware and examples are included in that EXE; you do not need to
select a separate firmware file or install an audio plugin.

Save pending edits, close Catalyst Edit and other Line 6 managers, including
POD Go NAM Manager, and connect the Catalyst by USB. Keep Master and headphone
levels low. If Windows does not detect the amplifier, check its cable and the
official driver available from [Line 6](https://line6.com/software/).

Initial installation in this version is enabled for **Catalyst 60/CX60**.
100/200 models have not been physically tested and their installation remains disabled.

## 2. Install Custom Firmware once

Open **FIRMWARE CF** and click its single **Install Custom Firmware** button.
With compatible CF already recognized, it is labelled **Reinstall Custom Firmware**.

It includes **the same demo capture in NAM 1 and NAM 2** and **three demo IRs**.
The app verifies a twelve-preset backup before installation. Keep power, USB
and the application connected until completion.

When prompted, switch the amplifier off and on normally, **without holding any
buttons**, then click **Comprobar arranque** (check startup). Editor tabs unlock
after the app confirms compatible CF.

Reinstalling restores all five examples. Keep your original NAM and IR files
to upload them again afterwards. A preset does not replace those source files.

## 3. Change a NAM or IR without reinstalling

Open **CHANGE NAM / IR**:

1. Choose **NAM 1, NAM 2, IR1, IR2 or IR3** and click **1. Load**, or drop a file.
2. Click **2. Connect to amp**.
3. Click **3. Send** to the chosen position and wait for confirmation.

Only that position changes; the other four are preserved. You do not need to
fill every position. Loading a file prepares it on the PC: uploading starts
when you click Send. **Clear** clears the local selection, not the amplifier's content.

NAM requires **A2 Lite, mono, 48 kHz**. Adjustable **NAM Input** starts at
**−5 dB**, affecting saturation and response. Output starts in **manual mode at
−3 dB**; Automatic is also available. Upload the NAM again to apply other settings.

Decodable IRs are converted to mono, 48 kHz and 2,048 samples. Initial cuts are
**Low Cut 80 Hz / High Cut 8,000 Hz**, adjustable before uploading.
Original files are unchanged.

## 4. Select a sound and save your preset

Return to **SOUND**, choose a bank and A or B channel, select **NAM 1 or NAM 2**
under AMPLIFIER / NAM, and choose your IR. Adjust Gain, EQ, preamp and volume.
Click **Save preset** and wait for confirmation before changing presets.

NAM runs inside the amplifier and reaches the speaker and direct outputs.
Cabinet IRs apply to **headphones, Direct Out and USB**; the physical speaker
keeps its own route. No VST or PC audio processing is required.

If an operation reports an error or remains pending, keep the message and read
the [FAQ](FAQ.md#english) or [Recovery](RECUPERACION.md#english). Do not restart
during an active transfer.
