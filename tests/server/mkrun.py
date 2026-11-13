# Creates and runs an isolated test server: mkrun.py <absolute run dir> <earth|vanilla> <hydro|control> <port> [scenarios file]
# HYDRO_CONFIG='{"FillVessels": true}' writes ModConfig/hydrology.json (Hydrology's switches) into the run.
# MAGIC='{"UncompressedChunkTTL": 1500}' overrides engine magic numbers (servermagicnumbers-default.json, from a 1.22.7 run).
# FAKE_CW=<a run's wateraudit\cylinder.json> replaces Cylinder World with tests/server/fakecylinder (its geometry only;
#   it reports version 1.0.4 and the config switches Earth 2.0's preflight reads, all on). Build it first (dotnet build).
# REUSE_SAVE=<a run's Saves\watertest.vcdbs> opens a copy of that save; WA_PROBE='[[x,y,z],...]' logs those blocks and stops.
# WA_AREA='[[x,z,half,yLow,yHigh],...]' writes every column's fluids in those squares to wateraudit/area.json (probe mode,
#   no scenarios; WA_WAKE_AREA=<seconds>: then wake its water and write area-woken.json that much later), or to area-t0.json
#   and area-end.json around a scenario run. WA_SHAPE_RADIUS: the shape check's half-width.
# SEED=<world seed> (default 386217).
# WA_PLAN_AREA=<half-width>: every plan's square of fluids at the end (area-plans.json).
# FAKE_QUERY=1 adds tests/server/fakeriverquery (Earth's river query, version 1, over the labs' QueryRiver areas).
# WORLD_ATTRS='{"hydrologyFillVessels": true}' adds world-config attributes (as Earth 0.16.8 sets on its worlds).
# WA_BEDSCAN='[[lat,lon,half],...]' (Earth worlds; HY-0004): probe mode that lists every fresh-water cell with open
#   air under it, or beside it below the ground (a cave opening under or beside river water), to wateraudit/bedscan.json;
#   WA_BEDSCAN_WAKE=<seconds>: then wake those cells and write bedscan-woken.json that much later.
#   WA_BEDSCAN_CAVES=1: first carve a 3 x 3 x 3 cave under one river column per square (a control, and the case itself).
# Needs Hydrology built (dotnet build in the project root, bin/Debug/mod) and wateraudit built (dotnet build -c Release in
# wateraudit/). Earth worlds take the newest earth-*.zip and cylinderworld-*.zip in the player's
# %APPDATA%\VintagestoryData\Mods (EARTH_ZIP, CWORLD_ZIP to override; either may also be a mod folder, which is copied,
# never modified). The script prints which ones it took.
# Earth 2.0 makes worlds only of its own play styles: PLAY_STYLE (default earth, 1:100; earth-10, earth-25, earth-50,
# earth-200) picks one, and the world takes that play style's settings from the package's worldconfig.json (the Earth
# tab's defaults: scale, height, rivers). MAP_HEIGHT overrides the play style's world height. An older Earth without play
# styles gets the old world (worldWidth EARTH_WIDTH, default 256000; MapSizeY MAP_HEIGHT, default 384).
# EARTH_DATA=<a ModData/earth folder> (default the player's %APPDATA%\VintagestoryData\ModData\earth when it exists; ""
#   for none): Earth 2.0's data store for the run. Its data/, ground/ and cache/ are copied; glo90/ (8.5 GB) is linked as a
#   directory junction and only read. Remove a run dir with `mkrun.py --remove <run dir>`, which unlinks the junction
#   first: deleting the run dir recursively by other means may follow the junction into the player's heights.
# EXTRA_MODS=<zips or mod folders, separated by os.pathsep>: also loaded (Sea, Earth 1.0's data packs).
# The run dir must be absolute: the server runs with the game folder as its working directory.
import json, sys, os, re, shutil, subprocess, zipfile, glob

def is_link(path):
    """A symbolic link or a directory junction (os.path.islink misses junctions before Python 3.12)."""
    return os.path.islink(path) or getattr(os.path, "isjunction", lambda p: False)(path) \
        or (os.path.lexists(path) and os.path.normcase(os.path.realpath(path)) != os.path.normcase(os.path.abspath(path)))

def remove_run(run):
    """Deletes a run dir: unlinks the glo90 junction (never following it), then the rest."""
    link = os.path.join(run, "ModData", "earth", "glo90")
    if os.path.lexists(link) and is_link(link):
        subprocess.run(["cmd", "/c", "rmdir", link], check=True)
    assert not os.path.lexists(link) or not is_link(link), "junction still there: " + link
    shutil.rmtree(run)
    print("removed", run)

if len(sys.argv) == 3 and sys.argv[1] == "--remove":
    remove_run(os.path.abspath(sys.argv[2]))
    sys.exit(0)

run, world, mode, port = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4])
here = os.path.dirname(os.path.abspath(__file__))
project = os.path.abspath(os.path.join(here, "..", ".."))
game = os.path.join(os.environ["APPDATA"], "Vintagestory")
player_data = os.path.join(os.environ["APPDATA"], "VintagestoryData")

def version_key(path):
    """Sorts mod zips by version (1.1.0-rc.3 after 1.0.4, 2.0.0 after 2.0.0-rc.7), then by time."""
    m = re.search(r"-(\d+)\.(\d+)\.(\d+)(?:-(.*?))?\.zip$", os.path.basename(path))
    if not m: return ((0, 0, 0), 0, (), os.path.getmtime(path))
    pre = m.group(4)
    parts = tuple((0, int(t), "") if t.isdigit() else (1, 0, t) for t in re.split(r"[.-]", pre)) if pre else ()
    return (tuple(int(g) for g in m.group(1, 2, 3)), 0 if pre else 1, parts, os.path.getmtime(path))

def newest(pattern):
    found = sorted(glob.glob(os.path.join(player_data, "Mods", pattern)), key=version_key)
    return found[-1] if found else None

earth_zip = os.environ.get("EARTH_ZIP") or newest("earth-*.zip")
cworld_zip = os.environ.get("CWORLD_ZIP") or newest("cylinderworld-*.zip")
assert os.path.isabs(run) and not os.path.exists(run), run
if world == "earth":
    assert earth_zip and os.path.exists(earth_zip), "no Earth package: set EARTH_ZIP"
    assert os.environ.get("FAKE_CW") or (cworld_zip and os.path.exists(cworld_zip)), "no Cylinder World package: set CWORLD_ZIP"
    print("Earth:", earth_zip)
    print("Cylinder World:", "fake (FAKE_CW)" if os.environ.get("FAKE_CW") else cworld_zip)

def read_package_file(package, name):
    """A file from a mod zip or mod folder, or None."""
    if os.path.isdir(package):
        path = os.path.join(package, name)
        return open(path, encoding="utf-8-sig").read() if os.path.exists(path) else None
    with zipfile.ZipFile(package) as z:
        return z.read(name).decode("utf-8-sig") if name in z.namelist() else None
for d in ("Mods", "ModConfig", "Saves"): os.makedirs(os.path.join(run, d))
# HELPER=reverttest: the minimal revert test (tests/server/reverttest) instead of the water audit (mode control: no Hydrology)
helper = os.environ.get("HELPER", "wateraudit")
shutil.copytree(os.path.join(here, helper, "bin", "mod"), os.path.join(run, "Mods", helper))
if os.environ.get("FAKE_QUERY"):
    # Earth's river query without Earth (never beside Earth: same mod system name)
    assert world != "earth", "FAKE_QUERY is for vanilla worlds only"
    shutil.copytree(os.path.join(here, "fakeriverquery", "bin", "mod"), os.path.join(run, "Mods", "fakeriverquery"))
    json.dump({"Version": int(os.environ["FAKE_QUERY"])}, open(os.path.join(run, "ModConfig", "fakeriverquery.json"), "w"))
if mode == "hydro":
    # HYDRO_MOD: another Hydrology mod folder (an older build, for bisecting)
    shutil.copytree(os.environ.get("HYDRO_MOD") or os.path.join(project, "bin", "Debug", "mod"), os.path.join(run, "Mods", "hydrology"))
fake_cw = os.environ.get("FAKE_CW")
if world == "earth" and fake_cw:
    # Cylinder World's geometry without its runtime (tests/server/fakecylinder), from a real run's wateraudit/cylinder.json
    shutil.copytree(os.path.join(here, "fakecylinder", "bin", "mod"), os.path.join(run, "Mods", "fakecylinder"))
    shutil.copy(fake_cw, os.path.join(run, "ModConfig", "fakecylinder.json"))
if world == "earth":
    for z in ((earth_zip,) if fake_cw else (earth_zip, cworld_zip)):
        if os.path.isdir(z): shutil.copytree(z, os.path.join(run, "Mods", os.path.basename(os.path.normpath(z))))
        else: shutil.copy(z, os.path.join(run, "Mods"))
    for name in ("earthmap.json", "earth.json"):
        json.dump({"MoveStoryLocations": False, "SelfTest": {"Enabled": False}}, open(os.path.join(run, "ModConfig", name), "w"), indent=2)
for z in filter(None, os.environ.get("EXTRA_MODS", "").split(os.pathsep)):
    if os.path.isdir(z): shutil.copytree(z, os.path.join(run, "Mods", os.path.basename(os.path.normpath(z))))
    else: shutil.copy(z, os.path.join(run, "Mods"))
sc = json.load(open(sys.argv[5] if len(sys.argv) > 5 else os.path.join(here, "scenarios.json")))
ac = {"Enabled": True, "StartDelayMs": 5000, "Shutdown": True, "Scenarios": sc}
if os.environ.get("WA_VESSELS_ATTR"): ac["SetVesselsAttr"] = True
if os.environ.get("WA_WAKEALL"): ac["WakeAllSec"] = int(os.environ["WA_WAKEALL"])
if os.environ.get("WA_PROBE"): ac["Probe"] = json.loads(os.environ["WA_PROBE"])
if os.environ.get("WA_AREA"): ac["ProbeArea"] = json.loads(os.environ["WA_AREA"])
if os.environ.get("WA_WAKE_AREA"): ac["WakeProbeAreaSec"] = int(os.environ["WA_WAKE_AREA"])
if os.environ.get("WA_POKE"): ac["ProbePoke"] = json.loads(os.environ["WA_POKE"])  # with WA_WAKE_AREA: [x, y, z] poked instead
if os.environ.get("WA_SHAPE_RADIUS"): ac["ShapeRadius"] = int(os.environ["WA_SHAPE_RADIUS"])
if os.environ.get("WA_PLAN_AREA"): ac["PlanAreaRadius"] = int(os.environ["WA_PLAN_AREA"])
if os.environ.get("WA_DUMPS"): ac["DumpAtSec"] = [int(v) for v in os.environ["WA_DUMPS"].split(",")]
if os.environ.get("WA_BEDSCAN"): ac["BedScan"] = json.loads(os.environ["WA_BEDSCAN"])
if os.environ.get("WA_BEDSCAN_WAKE"): ac["BedScanWakeSec"] = int(os.environ["WA_BEDSCAN_WAKE"])
if os.environ.get("WA_BEDSCAN_CAVES"): ac["BedScanCaves"] = True
json.dump(ac, open(os.path.join(run, "ModConfig", "wateraudit.json"), "w"), indent=2)
if os.environ.get("HYDRO_CONFIG"): open(os.path.join(run, "ModConfig", "hydrology.json"), "w").write(os.environ["HYDRO_CONFIG"])
d = json.load(open(os.path.join(here, "serverconfig-template.json"), encoding="utf-8-sig"))
d["Ip"] = "127.0.0.1"; d["Port"] = port; d["AdvertiseServer"] = False; d["Upnp"] = False; d["LastLaunchMods"] = None
d["ModPaths"] = ["Mods", os.path.join(run, "Mods")]
wc = d["WorldConfig"]
wc["Seed"] = os.environ.get("SEED", "386217"); wc["SaveFileLocation"] = os.path.join(run, "Saves", "watertest.vcdbs"); wc["WorldName"] = f"Hydrology test {world} {mode}"
if world == "earth":
    styles = json.loads(read_package_file(earth_zip, "worldconfig.json") or "{}").get("playStyles", [])
    style = os.environ.get("PLAY_STYLE", "earth")
    match = [p for p in styles if p.get("code") == style]
    if match:
        # Earth 2.0: the play style's own settings (its Earth tab defaults), survival, the world's edge blocked
        wc["PlayStyle"] = style
        wc["WorldConfiguration"] = dict(match[0].get("worldConfig", {}))
        wc["WorldConfiguration"].update({"worldEdge": "blocked", "gameMode": "survival"})
        wc["MapSizeY"] = int(os.environ.get("MAP_HEIGHT") or wc["WorldConfiguration"].get("earthWorldHeight", 384))
        if "MAP_HEIGHT" in os.environ: wc["WorldConfiguration"]["earthWorldHeight"] = wc["MapSizeY"]
    else:
        assert not styles, "the Earth package has no play style " + style + ": " + ", ".join(p.get("code", "?") for p in styles)
        # older Earth (before play styles): a 256,000-wide world
        wc["MapSizeY"] = int(os.environ.get("MAP_HEIGHT", "384"))
        ew = int(os.environ.get("EARTH_WIDTH", "256000"))
        wc["WorldConfiguration"] = {"worldWidth": str(ew), "worldLength": str(ew * 2 // 5), "worldEdge": "blocked", "startingClimate": "temperate", "gameMode": "survival"}
        if "EARTH_WIDTH" in os.environ: wc["WorldConfiguration"]["earthWorldSize"] = str(ew)  # the Earth tab's size (decision 0023)
        if world != "earth" and os.environ.get("PLAY_STYLE"): wc["PlayStyle"] = os.environ["PLAY_STYLE"]
    data = os.environ.get("EARTH_DATA", os.path.join(player_data, "ModData", "earth"))
    if data and os.path.isdir(data):
        dest = os.path.join(run, "ModData", "earth")
        os.makedirs(dest)
        for sub in ("data", "ground", "cache"):
            if os.path.isdir(os.path.join(data, sub)): shutil.copytree(os.path.join(data, sub), os.path.join(dest, sub))
        if os.path.isdir(os.path.join(data, "glo90")):
            subprocess.run(["cmd", "/c", "mklink", "/J", os.path.join(dest, "glo90"), os.path.join(data, "glo90")], check=True, stdout=subprocess.DEVNULL)
        print("Earth data:", data, "(glo90 linked: remove the run with mkrun.py --remove)")
else:
    wc["MapSizeY"] = 256
    d["MapSizeX"] = d["MapSizeZ"] = 1024000
    wc["WorldType"] = "standard"; wc["PlayStyle"] = "surviveandbuild"
    wc["WorldConfiguration"] = {"worldWidth": "1024000", "worldLength": "1024000", "gameMode": "survival"}
if os.environ.get("PLAY_STYLE"): wc["PlayStyle"] = os.environ["PLAY_STYLE"]
if os.environ.get("WORLD_ATTRS"): wc["WorldConfiguration"].update(json.loads(os.environ["WORLD_ATTRS"]))
if os.environ.get("REVERT_CFG"): open(os.path.join(run, "ModConfig", "reverttest.json"), "w").write(os.environ["REVERT_CFG"])
if os.environ.get("MAGIC"):
    # engine tuning (servermagicnumbers.json), e.g. MAGIC='{"UncompressedChunkTTL": 1500}', on top of a full default file
    mg = json.load(open(os.path.join(here, "servermagicnumbers-default.json"), encoding="utf-8-sig"))
    mg.update(json.loads(os.environ["MAGIC"]))
    json.dump(mg, open(os.path.join(run, "servermagicnumbers.json"), "w", encoding="utf-8"), indent=2)
if os.environ.get("REUSE_SAVE"):
    # open a copy of a finished run's save (probe mode); the copy keeps the original run untouched
    shutil.copy(os.environ["REUSE_SAVE"], wc["SaveFileLocation"])
json.dump(d, open(os.path.join(run, "serverconfig.json"), "w", encoding="utf-8"), indent=2)
with open(os.path.join(run, "stdout.log"), "w") as out:
    rc = subprocess.run([os.path.join(game, "VintagestoryServer.exe"), "--dataPath", run], cwd=game, stdout=out, stderr=subprocess.STDOUT, timeout=5400).returncode
print("done", run, "exit", rc)
