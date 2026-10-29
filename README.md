# VNyan Adaptive Lighting

A VNyan plugin that adjusts your avatar's lighting to match your game, so you look lit by your screen. Dark scenes dim the avatar and bright scenes brighten it, while your avatar's colors stay natural.

## Requirements

- [VNyan](https://suvidriel.itch.io/vnyan)
- [OBS Studio](https://obsproject.com/)
- [Spout2 Plugin](https://github.com/Off-World-Live/obs-spout2-plugin)

## Install

1. Download the zip from the [latest release](https://github.com/LuunaVR/VNyan-Adaptive-Lighting/releases/latest).
2. Close VNyan, then extract `VNyanAdaptiveLighting.dll` into `VNyan\Items\Assemblies`.
3. Start VNyan.

## OBS setup

1. Add or use an existing source, such as a Game Capture or Display Capture.
   - Its average brightness drives the lighting.
2. Right-click it, choose **Filters**, and add a **Spout Filter** under Effect Filters.
3. Set the sender name to `VNyanAdaptiveLighting_Spout`.
4. In VNyan, open **Plugins** and select **VNyan Adaptive Lighting**.
5. On the **Source** tab, select your sender.

## License

[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International](LICENSE)

Free to use and modify, not for commercial use. Modified versions must use the same license.
