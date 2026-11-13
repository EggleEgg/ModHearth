-- Used by ModHearth to open Dwarf Fortress's new-world setup after launching the game.
--
-- The script is polled by DwarfFortressAutoNewWorldRunner. It prints exactly one RESULT line:
--   waiting      - the title screen is not ready yet, or its menu text has not been drawn
--   clicked      - "Create new world" was clicked; the poller verifies the setup screen next
--   already-open - the new-world setup screen is already visible
--   game         - a fortress/adventure game is loaded; refuse to send input into the player's session
--
-- Usage (through DFHack's lua command):
--   lua -f AutoCreateNewWorld.lua

local gui = require('gui')

local function read_screen_rows()
    local ok, width, height = pcall(dfhack.screen.getWindowSize)
    if not ok or not width or not height then
        return nil, 0
    end

    local rows = {}
    for y = 0, height - 1 do
        local row = {}
        for x = 0, width - 1 do
            local pen = dfhack.screen.readTile(x, y)
            local ch = pen and pen.ch or 0
            row[x + 1] = (ch >= 32 and ch <= 126) and string.char(ch) or ' '
        end
        rows[y] = table.concat(row)
    end

    return rows, height
end

local function click_text(rows, height, needle)
    needle = needle:lower()
    for y = 0, height - 1 do
        local line = rows[y]:lower()
        local start_index, end_index = line:find(needle, 1, true)
        if start_index then
            local click_x = start_index - 1 + math.floor((end_index - start_index + 1) / 2)
            df.global.gps.mouse_x = click_x
            df.global.gps.mouse_y = y
            gui.simulateInput(dfhack.gui.getCurViewscreen(), '_MOUSE_L')
            return true
        end
    end

    return false
end

local scr = dfhack.gui.getDFViewscreen(true)
if scr == nil then
    print('RESULT=waiting')
    return
end

if df.viewscreen_new_regionst:is_instance(scr) then
    print('RESULT=already-open')
    return
end

if df.viewscreen_titlest:is_instance(scr) then
    local rows, height = read_screen_rows()
    if rows and click_text(rows, height, 'Create new world') then
        print('RESULT=clicked')
    else
        print('RESULT=waiting')
    end
    return
end

-- At the title screen and during DFHack startup, gamemode is NONE (3). A loaded
-- fortress or adventure game uses DWARF (0) or ADVENTURE (1); never click there.
local gamemode = df.global.gamemode
if gamemode == df.game_mode.DWARF or gamemode == df.game_mode.ADVENTURE then
    print(('DETAIL gamemode=%s screen=%s'):format(tostring(gamemode), tostring(scr._type)))
    print('RESULT=game')
    return
end

print('RESULT=waiting')
