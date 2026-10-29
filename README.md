# VNyan Adaptive Lighting

A VNyan plugin that adjusts your avatar's lighting to match your game, so you look lit by your screen. Dark scenes dim the avatar and bright scenes brighten it, while your avatar's colors stay natural.

## Requirements

- VNyan
- OBS Studio with the [Spout2 plugin for OBS](https://github.com/Off-World-Live/obs-spout2-plugin)

## Install

1. Download `VNyanAdaptiveLighting.zip` from the [latest release](https://github.com/LuunaVR/VNyan-Adaptive-Lighting/releases/latest).
2. Close VNyan, then extract `VNyanAdaptiveLighting.dll` into `VNyan\Items\Assemblies`.
3. Start VNyan and open **Plugins → VNyan Adaptive Lighting**.

## OBS setup

1. Add a source that shows your game, such as a Display Capture of the game's monitor.
2. Right-click it, choose **Filters**, and add a **Spout Filter** under Effect Filters.
3. Set the sender name to `VNyan_AvatarLighting`.
4. In the plugin's **Source** tab, pick that sender from the dropdown.

Tips:

- If the source is only used for lighting, put it in its own scene that you never show on stream and cover it with a Color Source.
- Delay filters on a source also delay the lighting. Use a separate source without delay for the Spout Filter.
- The source has to be visible somewhere in OBS, or it stops sending.

## Settings

| Setting | What it does |
| --- | --- |
| Mode | Monochromatic uses neutral white light. Colored lets screen colors tint the avatar slightly. |
| Minimum | Brightness when the screen is black. |
| Normal | Brightness for an ordinary scene. Also used when there is no signal. |
| Maximum | Brightness when the screen is white. |
| Smoothness | Higher values change the lighting more slowly. |
| Color influence | How strongly colors tint the avatar in Colored mode. |

Settings apply immediately and are saved automatically. The **Debug** tab has live values and test buttons.

## Notes

VNyan has no brightness control for plugins, so the plugin scales the scene's lights and ambient light. Anything else lit by those lights, such as props, changes with the avatar. Unlit and baked lighting is not affected.
