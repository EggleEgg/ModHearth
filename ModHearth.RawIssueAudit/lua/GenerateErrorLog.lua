-- Development-only DFHack automation used by ModHearth.RawIssueAudit.
--
-- Drives Dwarf Fortress from the title screen through "Create new world" -> "Create world",
-- selects a Pocket-sized world with 5 years of history so generation stays short, lets worldgen
-- finish, then keeps the generated world and returns to the title screen. The C# runner removes
-- the generated save folder afterwards. Opening the new-world screen and completing worldgen
-- forces DF to load and finalize every active raw, which is what writes the diagnostics the audit
-- reads.
--
-- Usage (through DFHack's lua command):
--   lua -f GenerateErrorLog.lua <status-file> <timeout-ms>
--
-- The status file is append-only. It receives START, periodic TICK lines, and exactly one of
-- DONE or "ERROR: <reason>". The C# runner polls the file instead of holding the RPC connection
-- open while DF loads raws or generates a world.

local gui = require('gui')

local status_path = ...
local timeout_ms = tonumber((...)) or 180000
if status_path == nil or status_path == '' then
    qerror('status file path is required')
end

local start_ms = dfhack.getTickCount()
local phase = 'start'
local phase_started_ms = start_ms
local finished = false
local modal_dismissed = 0
local pocket_clicked = false
local history_clicked = false
local world_clicked = false
local world_started_ms = 0
local last_tick_ms = 0
local loop_count = 0

local function write_status(line)
    local f = io.open(status_path, 'a')
    if f then
        f:write(line .. '\n')
        f:close()
    end
end

local function fail(reason)
    if finished then
        return
    end

    finished = true
    write_status('ERROR: ' .. reason)
    print('AUTOMATION-ERROR: ' .. reason)
end

local function finish()
    if finished then
        return
    end

    finished = true
    write_status('DONE')
    print('AUTOMATION-DONE')
end

local function read_screen_rows()
    local w, h = dfhack.screen.getWindowSize()
    local rows = {}
    for y = 0, h - 1 do
        local row = {}
        for x = 0, w - 1 do
            local pen = dfhack.screen.readTile(x, y)
            local ch = pen and pen.ch or 0
            row[x + 1] = (ch >= 32 and ch <= 126) and string.char(ch) or ' '
        end
        rows[y] = table.concat(row)
    end
    return rows, w, h
end

local function has(rows, needle)
    needle = needle:lower()
    for _, line in pairs(rows) do
        if line:lower():find(needle, 1, true) then
            return true
        end
    end
    return false
end

local function find_text(rows, height, needle)
    needle = needle:lower()
    for y = 0, height - 1 do
        local line = rows[y]:lower()
        local s, e = line:find(needle, 1, true)
        if s then
            return s - 1, y, e - s + 1
        end
    end
    return nil
end

local function click_text(rows, height, needle)
    local x, y, len = find_text(rows, height, needle)
    if not x then
        return false
    end

    local click_x = x + math.floor(len / 2)
    df.global.gps.mouse_x = click_x
    df.global.gps.mouse_y = y
    gui.simulateInput(dfhack.gui.getCurViewscreen(), '_MOUSE_L')
    print(('CLICK %s at %d,%d'):format(needle, click_x, y))
    return true
end

local function current_screen()
    return dfhack.gui.getDFViewscreen(true)
end

local function is_title()
    local scr = current_screen()
    return scr ~= nil and df.viewscreen_titlest:is_instance(scr)
end

local function is_new_region_setup()
    local scr = current_screen()
    return scr ~= nil and df.viewscreen_new_regionst:is_instance(scr)
end

local function screen_name()
    local scr = current_screen()
    local t = scr and scr._type
    return t and tostring(t) or 'nil'
end

local function set_phase(new_phase)
    phase = new_phase
    phase_started_ms = dfhack.getTickCount()
    write_status(('PHASE %s'):format(new_phase))
end

local function step()
    if finished then
        return
    end

    local now = dfhack.getTickCount()
    if now - start_ms > timeout_ms then
        fail(('timeout after %d ms in phase %s'):format(now - start_ms, phase))
        return
    end

    -- Never touch a loaded fortress/adventure: the audit only regenerates raws from the title
    -- or new-world screen, and sending menu input into a game could disturb the player's session.
    if df.global.gamemode == 0 then
        fail('a saved game is loaded; return Dwarf Fortress to the title screen and retry')
        return
    end

    local rows, width, height = read_screen_rows()
    local title = is_title()
    local new_region = is_new_region_setup()

    -- A failed mod preset disables the Create world button. Dismissing the modal would only
    -- leave the automation waiting, so treat it as fatal immediately.
    if has(rows, 'Failed to load some mods from the preset') then
        fail('Dwarf Fortress reports that some mods in the active preset failed to load; fix the modlist and retry')
        return
    end

    if has(rows, 'Enter: Ok') then
        modal_dismissed = modal_dismissed + 1
        if modal_dismissed > 10 then
            fail('too many confirmation modals appeared; refusing to keep dismissing them')
            return
        end

        gui.simulateInput(dfhack.gui.getCurViewscreen(), 'SELECT')
        write_status(('TICK %s modal-dismissed=%d'):format(phase, modal_dismissed))
        dfhack.timeout(5, 'frames', step)
        return
    end

    if title then
        if phase == 'returning' then
            finish()
            return
        end

        if phase == 'start' then
            if click_text(rows, height, 'Create new world') then
                set_phase('setup')
            elseif now - phase_started_ms > 5000 then
                fail('title screen is visible but "Create new world" was not found')
                return
            end
        elseif phase == 'setup' and now - phase_started_ms < 5000 then
            -- Wait for the new-world screen to replace the title screen.
        else
            fail(('returned to the title screen unexpectedly in phase %s'):format(phase))
            return
        end
    elseif phase == 'start' and new_region then
        -- The preflight probe allows the new-world setup screen to be open already.
        set_phase('setup')
    end

    if phase == 'setup' and not title then
        if has(rows, 'Create world') then
            if not pocket_clicked then
                pocket_clicked = click_text(rows, height, 'Pocket')
            elseif not history_clicked then
                history_clicked = click_text(rows, height, '5 years')
            elseif not world_clicked then
                world_clicked = click_text(rows, height, 'Create world')
                if world_clicked then
                    world_started_ms = now
                    set_phase('worldgen')
                end
            end

            if not world_clicked and now - phase_started_ms > 60000 then
                fail('the new-world screen did not expose the Pocket / 5 years / Create world controls')
                return
            end
        elseif now - phase_started_ms > 60000 then
            fail(('the new-world setup screen is not responding (screen %s)'):format(screen_name()))
            return
        end
    elseif phase == 'worldgen' then
        if has(rows, 'Continue generating') then
            -- The world was paused (for example by the player); resume instead of waiting forever.
            _ = click_text(rows, height, 'Continue generating')
        elseif has(rows, 'Play now') or has(rows, 'Keep world and return to main menu') then
            write_status(('WORLDGEN-COMPLETE after %d ms'):format(now - world_started_ms))
            if click_text(rows, height, 'Keep world and return to main menu') then
                set_phase('returning')
            elseif click_text(rows, height, 'Back to main menu') then
                set_phase('returning')
            else
                fail('worldgen finished but the return-to-main-menu button was not found')
                return
            end
        elseif has(rows, 'Create world') then
            -- The click may not have registered yet; retry for a few seconds before giving up.
            if now - world_started_ms > 30000 then
                fail('the Create world button did not respond (the active modlist may be invalid)')
                return
            elseif now - world_started_ms > 2000 then
                if click_text(rows, height, 'Create world') then
                    world_started_ms = now
                end
            end
        end
    end

    loop_count = loop_count + 1
    if loop_count % 6 == 0 or now - last_tick_ms > 1000 then
        last_tick_ms = now
        write_status(('TICK %s title=%s new_region=%s screen=%s pocket=%s history=%s world=%s'):format(
            phase, tostring(title), tostring(new_region), screen_name(),
            tostring(pocket_clicked), tostring(history_clicked), tostring(world_clicked)))
    end

    dfhack.timeout(5, 'frames', step)
end

write_status('START')
dfhack.timeout(5, 'frames', step)
