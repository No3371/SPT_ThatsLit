# SPT_ThatsLit

This simulates how humen will have a hard time to see you in the dark, and spot you faster when you are lit up by lgihts for AIs.

Developed with SAIN installed, SAIN scale AI vision distance by day time, and this give them a chance (determined by your brightness) to overlook you.

Most of the time on outdoor maps during the day, your "lit score" remains neutral because the ambience lighting light you up quite evenly. But when the sun goes down, the score will react your how much you are lit.

The most direct way to feel it is enable Debug Info in the plugin configs and go to night factory, you'll see how the meters react.

The third meter roughly represent the effectiveness of how your visual presence to AIs is modified, but that doesn't means you are invisible with a -100%, it only makes AIs have quite a chance to overlook you.

Also noted that AIs don't always relies on their vision to detect you.

The "lit score" ranges from -1 to 1, there are multiple factors to consider in order to get the lowest score as possible:

- Even a bit lit up body surfaces will increase your score a lot
- Remain stably lit (less to no brightness change) decrease the score (so, moving between brightness and darkness, or getting lit by flashing lights, make you lose this bonus)
- Time and cloudiness matters
- Sudden brightness change increase the score (ex: walking into bright area or flashing lights)
- Yes, getting lit up flashlighs and gun flashes increase the score
- Sneak in the shadow with light or laser on is useless
- AIs using nvg are not affected

## Fun facts

- What we see in game is not what is actually observed by cameras. The presented visual is heavily processed.
- We don't really have body parts other than arms and legs.
- The mod create a camera to observe the player's body so it can know how the player is lit up.
- The above 3 facts combined, is the reason why the light detection is not die accurate. It involves a lot of stupid math to make a alright estimatation on all the maps matching the time and weather.
