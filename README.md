# RemoveMenuWithParameters

A NDMF plugin that removes selected VRChat avatar menu items and their associated parameters at build time.  
指定したVRChatアバターのメニュー項目と、そのパラメーターをビルド時に削除するNDMFプラグインです。

## Installation

### Using VCC (VRChat Creator Companion) or ALCOM
1. Open [this link](https://yuleo21.github.io/21tools/)
2. Click "Add to VCC"
3. Click "Open with VCC (or ALCOM)"
4. Add **RemoveMenuWithParameters** to your avatar project.

## Usage

1. Add the **Remove Menu with Parameters** component to the same GameObject as your `VRCAvatarDescriptor`.
2. Check the menu items you want to remove in the inspector.
3. Build the avatar — the checked items and their parameters will be removed automatically.

## Features

- **Menu Item Selection**: Displays the avatar's full expression menu tree in the inspector with checkboxes.
- **Submenu Support**: Checking a submenu removes all items and parameters beneath it.
- **Shared Parameter Retention**: Optionally keep parameters that are still used by remaining menu items.
- **NDMF Integration**: Runs before Modular Avatar during the build, so the menu structure matches what you see in the inspector.
- **Non-Destructive**: Original assets are never modified; all changes happen at build time via cloning.

## Requirements

- VRChat Avatars SDK >= 3.5.0
- NDMF >= 1.5.0
