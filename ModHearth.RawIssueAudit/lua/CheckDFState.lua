-- Development-only DFHack probe used by ModHearth.RawIssueAudit before automation.
--
-- Prints a single machine-readable STATE line so the C# runner can refuse to clear errorlog.txt
-- or drive the UI while a saved game or an in-progress worldgen is loaded.
--
-- Usage: lua -f CheckDFState.lua

local function has_text(needle)
    local ok, width, height = pcall(dfhack.screen.getWindowSize)
    if not ok or not width or not height then
        return false
    end

    needle = needle:lower()
    for y = 0, height - 1 do
        local row = {}
        for x = 0, width - 1 do
            local pen = dfhack.screen.readTile(x, y)
            local ch = pen and pen.ch or 0
            row[x + 1] = (ch >= 32 and ch <= 126) and string.char(ch) or ' '
        end

        if table.concat(row):lower():find(needle, 1, true) then
            return true
        end
    end

    return false
end

local scr = dfhack.gui.getDFViewscreen(true)
if scr == nil then
    print('STATE=starting')
    return
end

if df.viewscreen_titlest:is_instance(scr) then
    print('STATE=title')
    return
end

if df.viewscreen_new_regionst:is_instance(scr) then
    -- The setup screen still has the Create world button. A paused/running worldgen does not;
    -- treating it as ready would silently cancel an existing generation without reloading raws.
    if has_text('Create world') or has_text('Enter: Ok') then
        print('STATE=worldgen')
    else
        print('STATE=worldgen-active')
    end
    return
end

if df.global.gamemode == 0 then
    print('STATE=game')
    return
end

print('STATE=other:' .. tostring(scr._type))
