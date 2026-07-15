import json
import os
import re
import urllib.request
import urllib.error

def fetch_latest_release(repo_name, token_to_use, label):
    url = f"https://api.github.com/repos/{repo_name}/releases/latest"

    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "ModHearth-GitHub-Actions"
    }

    if token_to_use:
        headers["Authorization"] = f"Bearer {token_to_use}"

    print(f"\n===== {label} =====")
    print("Repository:", repo_name)
    print("URL:", url)
    print("Using token:", bool(token_to_use))

    body = None
    try:
        req = urllib.request.Request(url, headers=headers)
        with urllib.request.urlopen(req) as resp:
            body = resp.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        # If there are no releases yet (404), return empty gracefully
        if e.code == 404:
            print(f"No releases found for {repo_name} (404).")
            return {}
        
        # If token was bad (401/403), retry anonymously
        elif e.code in (401, 403) and "Authorization" in headers:
            print(f"Auth failed with status {e.code}. Retrying anonymously...")
            headers.pop("Authorization", None)
            try:
                req = urllib.request.Request(url, headers=headers)
                with urllib.request.urlopen(req) as resp:
                    body = resp.read().decode("utf-8")
            except Exception as retry_err:
                print(f"Anonymous retry also failed: {retry_err}")
                return {}
        else:
            print(f"HTTP Error {e.code}: {e.reason}")
            return {}
    except Exception as e:
        print(f"Unexpected connection error: {e}")
        return {}

    if body:
        try:
            return json.loads(body)
        except Exception as parse_err:
            print(f"Failed to parse JSON response: {parse_err}")
            return {}
    return {}

def main():
    repo = os.environ.get("GITHUB_REPOSITORY")
    token = os.environ.get("GITHUB_TOKEN")
    be_token = os.environ.get("BLEEDING_EDGE_TOKEN")
    event_name = os.environ.get("GITHUB_EVENT_NAME")

    if not repo:
        print("Error: GITHUB_REPOSITORY environment variable is missing.")
        return

    # --------------------------
    # Production Build Lookup
    # --------------------------
    main_release = fetch_latest_release(repo, token, "Production")
    main_tag = main_release.get("tag_name") or ""
    match_main = re.match(r"build-(\d+)", main_tag)

    default_start = 28
    main_build = int(match_main.group(1)) + 1 if match_main else default_start
    main_build = max(main_build, default_start)

    print("Computed main build:", main_build)

    # --------------------------
    # Version computation
    # --------------------------
    if event_name == "workflow_dispatch":
        final_version = str(main_build)
    else:
        be_release = fetch_latest_release("EggleEgg/ModHearth-Builds", 
                                          be_token or token, "Bleeding Edge")
        be_tag = be_release.get("tag_name") or ""
        pattern = rf"^{main_build}\.(\d+)$"

        print("Regex pattern:", pattern)
        print("Regex input:", repr(be_tag))

        match_be = re.match(pattern, be_tag)
        print("Regex matched:", bool(match_be))

        if match_be:
            sub_build = int(match_be.group(1)) + 1
        else:
            sub_build = 1

        final_version = f"{main_build}.{sub_build}"

    print("\nFinal version:", final_version)

    # Write output back to GitHub Actions step context
    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a", encoding="utf-8") as handle:
            handle.write(f"build_number={final_version}\n")
    else:
        print("Warning: GITHUB_OUTPUT is not set. Output was not saved.")

if __name__ == "__main__":
    main()