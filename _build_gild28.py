import subprocess, sys, os

os.chdir(r"D:\loym2\LyoMir2-master")
result = subprocess.run(
    ["dotnet", "build", "GameSvr/GameSvr.csproj",
     "--no-incremental", "-c", "Debug", "-v", "quiet"],
    capture_output=True, timeout=180
)
# Write raw bytes to file to avoid GBK issues
out = result.stdout + result.stderr
with open("_build_gild28_out.txt", "wb") as f:
    f.write(out)
sys.exit(result.returncode)
