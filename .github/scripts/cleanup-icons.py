import concurrent.futures
import os
import re
import subprocess
from pathlib import Path

"""Cleans up svgs and saves them as plain using inkscape CLI. Useful for reducing file size and inkscape svg bloat"""
"""To setup the inkscape CLI, make sure inkscape is installed and added to your system PATH. You can test it by running 'inkscape --version' in your terminal"""

def remove_xml_comments(file_path: Path) -> None:
    content = file_path.read_text(encoding="utf-8")
    # Matches <!-- and --> across multiple lines using [\s\S]*?
    cleaned_content = re.sub(r"<!--[\s\S]*?-->", "", content)
    file_path.write_text(cleaned_content, encoding="utf-8")


def process_svg_file(file_path: Path) -> tuple[Path, bool, str]:
    cmd = [
        "inkscape",
        str(file_path),
        "--actions=vacuum-defs",
        "--export-plain-svg",
        "--export-type=svg",
        f"--export-filename={file_path}",
    ]

    try:
        # Step 1: Run Inkscape cleanup and plain export
        subprocess.run(cmd, check=True, capture_output=True, text=True)

        # Step 2: Strip any remaining XML comments from the file
        remove_xml_comments(file_path)

        return file_path, True, ""
    except subprocess.CalledProcessError as e:
        return file_path, False, e.stderr.strip()
    except Exception as e:
        return file_path, False, str(e)


def clean_resources_svgs_parallel(directory_path: str = "resources", max_workers: int | None = None):
    base_dir = Path(directory_path)

    if not base_dir.is_dir():
        print(f"Directory '{directory_path}' does not exist.")
        return

    svg_files = list(base_dir.rglob("*.svg"))
    total_files = len(svg_files)

    if total_files == 0:
        print(f"No .svg files found in '{directory_path}'.")
        return

    # Default worker count matches available CPU cores
    workers = max_workers or os.cpu_count() or 4
    print(f"Found {total_files} SVG file(s). Processing with {workers} parallel threads...\n")

    success_count = 0

    # ThreadPoolExecutor is optimal here because threads spend their time waiting on external CLI subprocesses
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as executor:
        future_to_file = {executor.submit(process_svg_file, file): file for file in svg_files}

        for future in concurrent.futures.as_completed(future_to_file):
            file_path, success, error_msg = future.result()
            if success:
                print(f"[SUCCESS] {file_path}")
                success_count += 1
            else:
                print(f"[FAILED]  {file_path}: {error_msg}")

    print(f"\nFinished processing. {success_count}/{total_files} files successfully cleaned.")


if __name__ == "__main__":
    clean_resources_svgs_parallel("resources")