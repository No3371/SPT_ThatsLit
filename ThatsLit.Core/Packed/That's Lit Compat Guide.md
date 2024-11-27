# That's Lit Compat Guide

That's Lit Brightness module supports scopes/goggles/laser & flashlight by **\*.thatslitcompat.json** files.

Any **\*.thatslitcompat.json** file found anywhere inside the plugins folder will be loaded on game launch.

This means, you can easily make your own compatibility patch, and even distribute it or include it in your mods.

That's Lit compatibility is a template based system, the content of a **\*.thatslitcompat.json** file is quite simple:

```json
{
    "protocol": 1,         // required, don't need to change it
    "priority": 1,         // required, the bigger the latter loaded (and overwrite smaller ones if there are duplicates)
    "scopeTemplates": [],  // optional
    "scopes": [],          // optional
    "goggleTemplates": [], // optional
    "goggles": [],         // optional
    "deviceTemplates": [], // optional
    "devices": [],         // optional
    "extraDevices": []     // optional
}
```

As you may have noticed, the file consist of mainly 2 types of data: template and objects.

A template defines the actual properties, while an object defines which template applies to which item type ingame.

For example, if you look at the **default.thatslitcompat.json**, you'll see the first scope template is `vanilla_general_nv_scopes`:

```json
{
    "name": "vanilla_general_nv_scopes",
    "nightVision": {
        "nullification": 0.8,
        "nullificationDarker": 0.4,
        "nullificationExtremeDark": 0.3
    },
    "thermal": null,
    "_comment": null
}
```

This defines "a scope that makes darkness 80% less effective for bots using the scope, 40% when it's darker, 30% when it's extremely dark".

And you can see in `scopes`, the scope with Id `5b3b6e495acfc4330140bd88` (which is "Armasight Vulcan MG 3.5x Bravo night vision scope") use the `vanilla_general_nv_scopes`, so the scope makes darkness 80% less effective for bots using the scope, 40% when it's darker, 30% when it's extremely dark.

```json
{
    "id": "5b3b6e495acfc4330140bd88",
    "_comment": "scope_base_armasight_vulcan_gen3_bravo_mg_3",
    "template": "vanilla_general_nv_scopes"
}
```

the `_comment` value does not do anything, it's just there to help us identify the Id.

The pattern above also applies to goggles and devices (laser / flashlight).

Data for goggles are pretty similar to scopes, the only difference is you can configure the FOV range from the bot where the goggle being effective.

For devices, I suggest just go with 1.0 or 0.0. The numbers are multipliers to how the device affects the bot seeing you, values higher than 1 makes the player even more visible with the device activated.

Value between 0 and 1 is a bit meaningless.

> A laser is a laser, in night time, any visible laser is a very noticeable line in sight. And In daytime, visible lights and lasers don't really provides penalty because they only compensate the lit score to neutral range. It's correct to pay attention in daytime, just not because it increases brightness score, but because it impair your stealth like in foliage/grasses.

`extraDevices` is a bit more special. In Tarkov, there are mods that can function as laser or flashlight, even though they are not devices.

These type of mods have to be handled differently so they are separately recorded. But the data is identical to `devices`.



