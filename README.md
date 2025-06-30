# AQtion Demo Launcher

A small but powerful launcher for Action Quake 2 demo files using Q2PRO.

---

## Features

- Browse and play AQ2 demos from multiple sources (e.g. vrol.se, S3 buckets)
- Full S3 folder navigation with support for buckets containing thousands of demos
- Auto-download missing maps (`.pkz`) for demos (configurable map source)
- One-click Q2PRO/AQtion download and setup
- Remove Q2PRO/AQtion installation with a single click (if installed by the launcher)
- No unpacking needed — Q2PRO reads `.pkz` maps directly
- Modern, dark-themed UI
- Designed for Windows 10/11 x64 (.NET 8 Desktop)
- **Automatic update check** (configurable via `appsettings.json`)

---

## Getting Started

### 1. Download & Extract

- Download the latest release ZIP
- Extract all files to a folder

### 2. Configure Demo Sources

- Copy `appsettings.sample.json` to `appsettings.json`
- Edit `appsettings.json` to add your own demo sources, Q2PRO download URL, map pattern, S3 root, and update API URL:

```json
{
  "DemoSources": {
    "Scene": "https://your-demo-source-url-1/",
    "S3": "https://your-demo-source-url-2/"
  },
  "Q2ProZipUrl": "https://your-q2pro-download-url/",
  "MapZipUrlPattern": "https://your-map-source-url/maps/{0}.zip",
  "S3BucketRoot": "https://your-s3-bucket-root/",
  "UpdateApiUrl": "https://api.github.com/repos/yourusername/yourrepo/releases/latest"
}
```

> **Note:**  
> Never commit your real `appsettings.json` to public repositories.  
> Only share `appsettings.sample.json` with placeholder URLs.

### 3. Run the Launcher

- Double-click `AQtionDemoLauncher.exe`

---

## Usage

1. **If Q2PRO/AQtion is missing:**
   - Click **Download** to auto-install Q2PRO/AQtion, or
   - Click **Choose** to select your local Q2PRO/AQtion folder
   - Click **Remove** to delete a Q2PRO/AQtion installation downloaded by the launcher

2. **Browse demos** from the source list (S3 buckets support folder navigation)

3. **Click a demo** and hit **Play Demo**  
   - The launcher will auto-download missing maps if needed

4. **Use Back/Refresh** to navigate folders and update listings

5. **Automatic update check:**  
   - The launcher will notify you if a new version is available (based on the `UpdateApiUrl` in your config).

---

## Where Files Go

- **Demos:**  
  `<q2pro>\action\demos\`
- **Maps:**  
  `<q2pro>\action\<mapname>.pkz`

> `.pkz` = zipped map (bsp + textures + sounds).  
> No unpacking needed — Q2PRO reads them directly.

---

## Troubleshooting

- **.NET 8.0 Desktop Runtime required:**  
  [Download here](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

- **Game doesn't launch?**  
  - Make sure the demo is valid
  - You selected the correct Q2PRO/AQtion folder
  - The required map `.pkz` exists in your `action\` folder

- **Buttons disabled?**  
  - Download/Remove/Choose buttons are enabled/disabled automatically based on Q2PRO/AQtion status and installation method.

---

## Customization

- **Add new demo sources:**  
  Edit `appsettings.json` and restart the launcher.
- **Change map download source:**  
  Edit `MapZipUrlPattern` in `appsettings.json`.
- **Change update check:**  
  Edit `UpdateApiUrl` in `appsettings.json`.
- **Change theme:**  
  Edit the `ApplyQuake2Theme()` method in `Form1.cs`.

---

## .gitignore

- Add `appsettings.json` to your `.gitignore` to avoid committing secrets or private URLs.
- Only commit `appsettings.sample.json` with placeholder values.

---

## Credits

- AQ2SCENE Collective
- vrol.se - launcher logic & design
- AQTION / Q2PRO Devs - for the tech
- AQ2 Community - for keeping the frags flying

---

## Support

- For issues or suggestions, open an [issue](https://github.com/yourusername/aqtion-demo-launcher/issues) on GitHub.
- Greets to all OG fraggers and pickup legends!

---

**SUPPORT THE DEVELOPERS. THIS TOOL IS 100% LEGIT. USE RESPONSIBLY.**
