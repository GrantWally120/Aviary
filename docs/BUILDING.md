# Getting Aviary onto your PC

You never have to build Aviary yourself. GitHub does it for you, on a Windows machine
in the cloud, every time a change lands. Your job is to download the result.

This page is all clicking. There is nothing to type.

---

## What happens automatically

Every time new work is saved to the `main` branch, GitHub starts a fresh Windows
machine, builds Aviary, and leaves a `.zip` file attached to that build. The first
build of the day usually takes **5 to 10 minutes**.

The build includes everything Aviary needs to run. You do not install .NET, you do not
install a "Windows App SDK", you do not install anything at all. Windows 11 already has
the Microsoft Edge WebView2 component that Aviary uses to show web pages.

---

## Downloading the latest build

**1. Open the repository on github.com.**

**2. Click the `Actions` tab.**
It sits along the top of the page, in the row that starts with `Code`, `Issues`,
`Pull requests`. Click `Actions`.

**3. In the left sidebar, click `Build Aviary (win-x64)`.**
That is the name of the build. Clicking it filters the list to just Aviary builds.

**4. Click the run at the very top of the list.**
The list is newest first, so the top one is the most recent. Each row has a coloured
icon on the left:

| Icon | Meaning | What to do |
|---|---|---|
| Green tick | Built successfully | Carry on to step 5 |
| Yellow dot (spinning) | Still building | Wait a few minutes, refresh the page |
| Red cross | The build broke | See **When a build goes red** below |
| Grey slash | Cancelled, because a newer change came in right behind it | Use the run above it |

**5. Scroll to the bottom of that page.**
Below the box showing the build steps there is a section headed **Artifacts**.

**6. Click `Aviary-win-x64`.**
Your browser downloads `Aviary-win-x64.zip`. It is roughly 70–110 MB.

> If nothing happens when you click it, you are probably signed out. GitHub only lets
> signed-in users download artifacts. Sign in and try again.

---

## Installing it (there is no installer)

**1. Find `Aviary-win-x64.zip` in your Downloads folder.**

**2. Right-click the zip and choose `Properties`. Do this before you extract.**
At the bottom of the `General` tab, if you see a checkbox labelled **Unblock**, tick it
and click `OK`.

This matters. Windows marks anything downloaded from the internet as untrusted. If you
extract the zip while it is still marked, every single file inside inherits that mark
and Aviary may refuse to start or behave strangely. Unblocking the zip first clears it
for everything inside. If there is no Unblock checkbox, Windows already trusts it and
you can move on.

**3. Right-click the zip and choose `Extract All...`**

**4. Pick a permanent home, not the Downloads folder.**
Something like `Documents\Aviary`. Click `Extract`.

> **Do not run Aviary from inside the zip.** Windows will happily open a zip like a
> normal folder and let you double-click things inside it. Aviary will not work that
> way — it needs the several hundred files sitting next to it. Extract first, always.

**5. Open the extracted `Aviary` folder and double-click `Aviary.exe`.**

**6. The first time, Windows will try to stop you.**
You will see a blue box: *"Windows protected your PC"*. This is SmartScreen, and it
appears for every program that has not been code-signed with a paid certificate.
Aviary is not signed yet.

Click **`More info`**, then click **`Run anyway`**.

You only have to do this once per download.

---

## A note on zips inside zips

Ideally you download `Aviary-win-x64.zip` and it contains a folder called `Aviary`.

If instead you extract it and find *another* zip inside, just extract that one too.
GitHub occasionally wraps artifacts in a second zip of its own. It is harmless — unzip
twice and carry on.

---

## Keeping the app up to date

There is no auto-update. To get a newer Aviary, repeat the download steps above and
extract over the top of your existing folder, replacing the files when Windows asks.

If you would rather keep old versions around, extract each one into its own folder
(`Aviary 5 Sep`, `Aviary 12 Sep`, and so on).

**Do not drag `Aviary.exe` out of its folder on its own.** It will not run by itself.
Move the whole folder if you need to move it.

---

## Building on demand

If nothing has changed but you want a fresh build anyway:

1. Go to the `Actions` tab.
2. Click `Build Aviary (win-x64)` in the left sidebar.
3. On the right, click the grey **`Run workflow`** button.
4. Leave the branch set to `main` and click the green **`Run workflow`** button in the
   little panel that drops down.
5. Wait about ten seconds and refresh the page. A new run appears at the top of the
   list with a yellow spinning dot.

---

## When a build goes red

A red cross means the code did not compile. There is no zip to download from that run.

To find out why, and to hand the information to Claude:

1. Click the red run in the list.
2. Click the box named `Publish win-x64`. This opens the log.
3. Look for the step with a red cross next to it and click the little arrow to expand
   it. The useful part is usually the **first** red line, not the last.
4. Copy that red line and give it to Claude.

If the error is not obvious, there is a full diagnostic log attached to every run,
successful or not:

1. Scroll to the bottom of the run page to the **Artifacts** section.
2. Download **`build-logs`**.
3. Send that file to Claude. It contains everything the build machine saw.

Meanwhile, the previous green build is still sitting further down the list, and its zip
still works. A red build never breaks a copy you already downloaded.

---

## Housekeeping

- **App downloads are kept for 14 days.** After that the zip is deleted from that run
  and the row shows no artifact. The run itself stays in the list forever. If you want
  a build to last, download it and keep the zip somewhere on your PC.
- **Diagnostic logs are kept for 7 days.**
- The 14-day limit exists on purpose. Each build is around 90 MB, and GitHub gives a
  private repository a fixed amount of storage. Without a limit, a few weeks of pushes
  would fill it and then uploads start failing — which would mean no downloads at all.
- Each build is a complete, standalone copy of Aviary. That is why it is large. It is
  carrying its own .NET runtime and its own Windows App SDK so that your PC needs
  nothing installed.
