
### MiCellAnnGELo

A research focused Unity project for interactive visualisation and collaborative annotation of cellular data. It supports time series meshes and volumetric datasets such as multi frame TIFF with single and dual channel rendering. The scene is XR ready and includes multiplayer, basic voice, and utilities for saving and loading annotations.



### Requirements

1. Unity Editor 6000.0.53f1
2. For XR testing in the Meta Quest, make sure you are a registered developer in Meta's website and that your device is in developer mode.
    
    2.1. To enable developer mode, you have to be a registered "developer" on the Meta website, it's free. Open developer.oculus.com/sign-up/ and create an 'organisation'. You'll need to accept the developer agreement and to verify your account using either a credit card or a phone number. Most accounts will already be verified. Next you'll need to create an organisation like below.



    2.2. Open the Oculus app on your smartphone or tablet. In the Settings tab, tap on the headset and tap 'More settings'. In the list, you should now see Developer Mode. Once you have enabled developer mode it is a good idea to reboot the headset to be able to see it on the device.



    2.3. Now its time to connect the USB cable. Once you are connected you will have to allow USB debugging access on your headset. You should also select 'Always allow from this computer' to prevent this message from coming up every time you connect.

3. Supabase organisation (for cloud features)

   If you plan to use the project’s Supabase-backed features, set up a Supabase account and organisation first.

    3.1. Create an account and organisation at https://supabase.com
    
    3.2. Create a Project inside your organisation (any region/tier is fine for testing).
   
    3.3. Note your Project URL and API keys (Anon and Service Role) for later configuration in Unity.
   
    3.4. For up-to-date steps, see the Supabase documentation: https://supabase.com/docs

Packages are declared in `Packages/manifest.json` including Universal Render Pipeline, XR Management, OpenXR, XR Interaction Toolkit, XR Hands, Netcode for GameObjects, Unity Transport, Vivox, Input System and others. These are restored by Unity on first open.

### First time set up

1. Open Unity Hub and add the project folder `MiCellAnnGELo` or github link ``
2. Use the exact editor version 6000.0.53f1 when prompted by Hub
3. Let Unity import assets and restore packages on first launch
4. Open the scene at `Assets/Scenes/MainScene.unity`
5. In the top menu select File then Build Settings, select the platform you want to build for (Android/ Windows), then ensure the scene is added to the build list



7. If you plan to use XR then open `Project Settings` then `XR Plug in Management` then enable OpenXR for your target platform and keep the default interaction profiles



8. Ensure the Input System is active (Unity will prompt to restart the editor if switching from the legacy input)

### Running in the editor

1. Open `MainScene.unity`
2. Make sure `XR Device Simulator` is enabled in the scene
3. Press Play to enter the program in the editor
4. Use the in scene UI to join multiplayer, load data and enable tools described below

### Supported data

#### TIFF volumes

The project can load multi frame TIFF volumes using the BitMiracle LibTiff plugin. Dual channel volumes are supported. When dual channel mode is active you can control separate transfer functions for each channel (although the UI is not in place yet). The loader component is `TiffTimeSeriesLoader` and it appears in the main scene inside `Cell (volume based)`.

#### Image sequences

PNG and JPG sequences can be converted at runtime into a `VolumeDataset`. This is intended for smaller test volumes.

#### Mesh time series

PLY files are used for time series meshes. Optional CSV files can provide colour labels or marker annotations per frame.

### Core tools in the scene

#### Data loading with `FileHandler`

Use the Load Data buttons in the `Import Canvas` then choose a folder for a time series or select a TIFF file for a volume. On desktop the standard file system is used. On Android the project uses the Simple File Browser with Storage Access Framework when needed.

#### Volume rendering manager

`VolumeRenderingManager` controls the active volume material and quality. If a TIFF with two channels is loaded and Combine Channels is disabled the scene switches to dual channel transfer functions automatically. Gradient textures are generated on demand and the shader receives the dataset specific maximum gradient magnitude for proper scaling.


#### Annotation workflow

##### Saving or loading annotations

Use the annotation buttons in the `Import Canvas` to save marker CSV from the current session or to load a CSV generated earlier. The annotation manager coordinates Simple File Browser dialogues for choosing files.

#### Multiplayer and voice

The project includes Netcode for GameObjects and a manager `Network Manager VR Mutliplayer` in the Managers folder. The `XRI_Network_Game_Manager` prefab is used in the scene to authenticate and join a lobby during play mode. Vivox is included for basic voice if service credentials are configured in the editor Services window.

### Building

#### Android VR or PC VR

1. Install platform support in the Unity Hub for your target
2. In Project Settings enable OpenXR for the platform and keep default interaction profiles
3. MAKE SURE `XR Device Simulator` IS DISABLED BEFORE BUILDING



5. Build and run to your selected device

### Unity Packages and Versions

| Package | Version |
| --- | --- |
| com.unity.2d.sprite | 1.0.0 |
| com.unity.burst | 1.8.24 |
| com.unity.collab-proxy | 2.8.2 |
| com.unity.feature.development | 1.0.2 |
| com.unity.inputsystem | 1.14.2 |
| com.unity.learn.iet-framework | 4.2.0-pre.3 |
| com.unity.multiplayer.center | 1.0.0 |
| com.unity.multiplayer.center.quickstart | 1.0.1 |
| com.unity.multiplayer.tools | 2.2.6 |
| com.unity.multiplayer.widgets | 1.0.1 |
| com.unity.netcode.gameobjects | 2.4.4 |
| com.unity.render-pipelines.universal | 17.0.4 |
| com.unity.services.authentication | 3.5.1 |
| com.unity.services.multiplayer | 1.1.6 |
| com.unity.services.vivox | 16.6.2 |
| com.unity.transport | 2.5.3 |
| com.unity.xr.hands | 1.5.1 |
| com.unity.xr.interaction.toolkit | 3.2.1 | 
| com.unity.xr.management | 4.5.1 |
| com.unity.xr.openxr | 1.15.1 |
### Repository structure overview

`Assets/Scripts` gameplay and tools including volume importers, UI, annotation and networking

`Assets/Materials`, `Assets/Shaders` materials and shader graphs for volume rendering

`Assets/Prefabs` reusable scene content including Managers and UI

`Assets/Plugins` third party libraries such as BitMiracle LibTiff and Simple File Browser

### Credits

This project uses BitMiracle LibTiff NET and Simple File Browser, as well as a variety of Unity Packages (see table above).


