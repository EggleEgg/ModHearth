local modman = reqscript('gui/mod-manager')
local json = require('json')

local function has_mod_manager_focus(screen)
    local focus_strings = dfhack.gui.getFocusStrings(screen)
    for _,fs in ipairs(focus_strings) do
        if fs:find('mod%-manager') then
            return true
        end
    end
    return false
end

local function get_modlist_fields_wrapper()
    if modman.get_modlist_fields then
        return modman.get_modlist_fields
    end
    return get_modlist_fields
end

local function get_any_moddable_viewscreen_wrapper()
    if modman.get_any_moddable_viewscreen then
        return modman.get_any_moddable_viewscreen
    end
    return get_any_moddable_viewscreen
end

local function vanilla(dir)
    return dir and dir:startswith('data/vanilla')
end

local function copy_mod_entry(viewscreen, to, from, mod_id, mod_version, get_fields)
    local to_fields = get_fields(to, viewscreen)
    local from_fields = get_fields(from, viewscreen)

    local mod_index = nil
    for i, v in ipairs(from_fields.id) do
        local version = from_fields.numeric_version[i]
        local src_dir = from_fields.src_dir[i]
        if v.value == mod_id and (vanilla(src_dir) or version == mod_version) then
            mod_index = i
            break
        end
    end

    if mod_index == nil then
        return false
    end

    for k, v in pairs(to_fields) do
        if type(from_fields[k][mod_index]) == "userdata" then
            v:insert('#', from_fields[k][mod_index]:new())
        else
            v:insert('#', from_fields[k][mod_index])
        end
    end

    return true
end

local function clear_mods(viewscreen, get_fields)
    local active_modlist = get_fields('object_load_order', viewscreen)
    local avail_modlist = get_fields('available', viewscreen)
    for _, modlist in ipairs({active_modlist, avail_modlist}) do
        for _, v in pairs(modlist) do
            for i = #v - 1, 0, -1 do
                v:erase(i)
            end
        end
    end
end

local function set_available_mods(viewscreen, loaded, get_fields)
    local base_avail = get_fields('base_available', viewscreen)
    local unused = {}
    for i, id in ipairs(base_avail.id) do
        if not loaded[id.value] then
            local version = base_avail.numeric_version[i]
            table.insert(unused, { id = id.value, version = version })
        end
    end

    for _, v in ipairs(unused) do
        local success = copy_mod_entry(viewscreen, "available", "base_available", v.id, v.version, get_fields)
        if not success then
            dfhack.printerr('[ModHearth] Failed to show ' .. v.id .. ' in available list')
        end
    end
end

local function apply_modlist_to_screen(viewscreen, modlist, get_fields)
    clear_mods(viewscreen, get_fields)

    local failures = {}
    local loaded = {}
    for _, v in ipairs(modlist) do
        local success = copy_mod_entry(viewscreen, "object_load_order", "base_available", v.id, v.version, get_fields)
        if not success then
            table.insert(failures, v.id)
        else
            loaded[v.id] = true
        end
    end

    set_available_mods(viewscreen, loaded, get_fields)
    return failures
end

local function get_default_modlist()
    local presets_file = json.open("dfhack-config/mod-manager.json")
    presets_file:read()
    if not presets_file.data or #presets_file.data == 0 then
        return nil
    end
    for _, v in ipairs(presets_file.data) do
        if v.default then
            return v
        end
    end
    return presets_file.data[1]
end

local scr = dfhack.gui.getCurViewscreen(true)
while scr do
    if has_mod_manager_focus(scr) then
        dfhack.println('[ModHearth] Reloading mod manager screen.')
        dfhack.screen.dismiss(scr)
        modman.ModmanageScreen{}:show()
        return
    end
    scr = scr.parent
end

local get_fields = get_modlist_fields_wrapper()
local get_viewscreen = get_any_moddable_viewscreen_wrapper()
local modlist_entry = get_default_modlist()
local mod_screen = get_viewscreen and get_viewscreen() or nil

if mod_screen and modlist_entry and modlist_entry.modlist then
    dfhack.println('[ModHearth] Applying default modlist to Mods screen.')
    local failures = apply_modlist_to_screen(mod_screen, modlist_entry.modlist, get_fields)
    if #failures > 0 then
        dfhack.println('[ModHearth] Failed mods: ' .. table.concat(failures, ', '))
        for _, v in ipairs(failures) do
        end
    end
else
    dfhack.println('[ModHearth] No reload performed.')
end
