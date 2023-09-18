# SPT_ThatsLit

Let AIs have a hard time too see you sneaking in the shadow, or notice you faster if you are lit up by lights.

This mod give AIs a chance (determined by your brightness) to overlook you, by adjusting a number called SeenCoef, which decides how long the an AI need to have you in their vision to perceive you.

Developed with SAIN installed, should work fine together unless things change.

Most of the time on outdoor maps during the day, your "lit score" remains neutral because the ambience lighting lights you up quite evenly, even under most shadows. But when the sun goes down, the score will start to react how you are lit up.

The most direct way to feel it is go to night factory and look at the maters at the top left corner, which is enabled by default. The first meter is the current "lit score", and the second meter shows how effective the score is affecting AIs.

You won't be invisible with a -1 (-100%) score, it only makes AIs have quite a chance to overlook you.

Also noted that AIs don't always relies on their vision to detect you.

The "lit score" ranges from -1 to 1, there are multiple factors to consider in order to get the lowest score as possible:

- Any lit up body surfaces will increase your score, even if most other parts are in the dark
- Remaining stably lit (less to no brightness change) decrease the score (so, moving between brightness and darkness, or getting lit by flashing lights, make you lose this bonus)
- Time, cloudiness and distance matter
- Sudden brightness change increase the score (ex: walking into bright area or under flashing lights)
- Yes, getting lit up flashlighs and gun flashes increase the score
- Sneaking in the shadow is useless when you left any light or laser on
- AIs using NVGs are quite unaffected by darkness
- Staying near multiple bushes makes AIs harder to see you from afar

The mod also introduce some randomness to AIs vision check regardless the "lit score" by rarely extremely increase the SeenCoef. In theory, this simulate how human space out or lose focus.
# ​Calibrated Maps

Every map has different ​lighting characteristic, so the light detection may need to be manually calibrated per map.

​Here is the list of calibrated maps, maps not listed here may have overly inaccurate detection:​

- ​Lighthouse
- Woods
- Factory
- Shoreline
- Reserve
- Customs

The calibration is a very very very tedious and time consuming process, if you find issues and want to help, turn on Debug Info, take screenshot and send it.

## Fun facts

- What we see in game is not what is actually observed by cameras. The presented visual is heavily processed.
- We don't really have body parts other than arms and legs, so a lot of light run through where the thorax should be.
- The mod create a camera to observe the player's body so it can know how the player is lit up.
- The above 3 facts combined, is the reason why the light detection is not die accurate. It involves a lot of damn stupid math to make a alright estimatation for different time and weather on all the maps.
- I knew nothing about how modding works for this game, like even how mod files should be structured, so I just download Soralint's SAIN then removed most his stuff and started to work on it. Also I took a helper class from SAIN, so a lot thanks to Solarint.