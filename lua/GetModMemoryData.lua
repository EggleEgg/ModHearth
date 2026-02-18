function GetAllMods()
    -- get manager and attempt to get viewScreen.
    local manager = reqscript('gui/mod-manager')
    local viewScreen =
        (manager.get_any_moddable_viewscreen and manager.get_any_moddable_viewscreen()) or
        (manager.get_moddable_viewscreen and manager.get_moddable_viewscreen('region')) or
        (manager.get_newregion_viewscreen and manager.get_newregion_viewscreen())

    -- Return a number starting with 0 on fail.
    if viewScreen == nil then return "01" end

    -- Get all available mods.
    local allAvailableMods = manager.get_modlist_fields('base_available', viewScreen)

    -- Loop through all available mods and add to outputData.
    local outputData = ""
    for i, v in ipairs(allAvailableMods.id) do

        -- Get non header data (not used right now).
        local name = allAvailableMods.name[i]
        local displayed_version = allAvailableMods.displayed_version[i]
        local id = v.value
        local mod_header = allAvailableMods.mod_header[i]
        
        local earliest_compat_numeric_version = allAvailableMods.earliest_compat_numeric_version[i]
        local numeric_version = allAvailableMods.numeric_version[i]
        local src_dir = allAvailableMods.src_dir[i].value

        -- Get other mod data from header
        local modHeaderString = "{"
        for k, v in pairs(mod_header) do
            -- Dependencies and such not handled for now.
            if tostring(k) == "dependencies" or tostring(k) == "dependency_type" or tostring(k) == "conflicts" or tostring(k) == "flags" or tostring(k) == "steam_tag" or tostring(k) == "steam_key_tag" or tostring(k) == "steam_value_tag" or string.find(tostring(k), "steamapi") then
                if (not string.find(tostring(k), "steamapi")) and (not tostring(k) == "flags") then
                    for k2,v2 in pairs(v) do
                    end
                end
            else
                local addition = tostring(v)
                addition = string.gsub(addition, '\\', '\\\\')
                addition = string.gsub(addition, '"', '\\"')
                modHeaderString = modHeaderString.."\n\""..tostring(k).."\": \""..addition.."\","
            end
        end
        -- Remove trailing comma from JSON.
        modHeaderString = modHeaderString:sub(1,-2)
        modHeaderString = modHeaderString .. "}"

        outputData = outputData..tostring(name).."|"..tostring(displayed_version).."|"..tostring(id).."|"..tostring(earliest_compat_numeric_version).."|"..tostring(numeric_version).."|"..tostring(src_dir).."==="..modHeaderString.."___"
    end

    -- Return outputData with the last comma removed.
    return outputData:sub(1,-4)
end

print(GetAllMods())
