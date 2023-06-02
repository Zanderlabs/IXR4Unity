# IXR4Unity
API to easily connect the [Intuitive XR Suite](https://github.com/Zanderlabs/IXR-Suite) to Unity.

# Usage
To get this to work you need to:

- download and run the [IXR Suite](https://github.com/Zanderlabs/IXR-Suite)
- make a new Unity project
- add LSL4Unity: Open the Package Manager Window, click on the + dropdown, and choose `Add package from git URL...`. Enter https://github.com/labstreaminglayer/LSL4Unity.git
- Add the IXR_VR_Controller script to your assets
- add an empty gameobject to the scene, call it e.g. `IXR4Unity`
- add the script to the empty gameobject
- in the IXR controller script you can then select either simulation or real BCI in the editor
- when you have a VR scene you can add the HMD and hands to the IXR controller to use them for simulating focus based on motion when the IXR suite is offline, but this is not mandatory

-> Run. In the editor, the `focus level` field of the IXR controller shows your current focus level.

You can then go ahead and make a whole game and add this to the game and use the focus value there.
