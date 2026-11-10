# tools/

Place the Tailwind CSS 4 standalone CLI here as `tailwindcss` (Linux/macOS) or `tailwindcss.exe` (Windows).
Download from https://github.com/tailwindlabs/tailwindcss/releases (asset `tailwindcss-<os>-<arch>`), rename, `chmod +x`.
The binary is git-ignored; CI downloads it in the build step. No Node is used anywhere.

Build the stylesheet:

    ./tools/tailwindcss -i src/Presentation/Salon.Web/Styles/app.css -o src/Presentation/Salon.Web/wwwroot/css/app.css --minify
