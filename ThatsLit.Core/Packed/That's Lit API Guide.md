# That's Lit API Guide
There are mainly 2 ways to access data in That's Lit:
- Directly references and depends on That's Lit, then you just use the **ThatsLitAPI** class
- Locate a method you want to call in **ThatsLitAPI** and call it through reflection (in optimized ways)

The first is simple and straight forward enough, anyone can do it with basic .NET development knowledge. 

We are going to talk about the 2nd approach:

Let's say you want to call `float GetBrightnessScore(Player)` in **ThatsLitAPI**, so you can access a player's current Brightness.

First, in your code, declare a **delegate** that matches the method signature:

```csharp
delegate float GetBrightnessScore (Player player);
```

This defines the "shape" of such method, we then need to declare an instance of this type of delegate:

```csharp
private GetBrightnessScore GetBrightnessScore { get; }
```

This means a delegate named GetBrightnessScore of type GetBrightnessScore.

You can make it a bit more understandable if you want:

```csharp
delegate float GetBrightnessScoreMethod (Player player);

private GetBrightnessScoreMethod GetBrightnessScore { get; set; }
```

How do we use such delegate?

```csharp
Player p;
this.GetBrightnessScore(p)
```

As you can see, it's exactly like using a normal method.

Now, the problem is, how do we use this to access our target: `ThatsLitAPI.GetBrightnessScore`?

Simple Reflection is known to be very slow, we need to use some technique to greatly reduce the overhead.

This is why we use delegate instead of just `MethodInfo`, we can just pay the cost once to create delegate from MethodInfo, assign it to the `GetBrightnessScore` property and then we can call it almost as cheap as calling a normal method.

```csharp
// AccessTools is provided by HarmonyLib
var methodInfo = AccessTools.Method(Type.GetType("ThatsLit.ThatsLitAPI, ThatsLit.Core"), "GetBrightnessScore", new Type[] { typeof(EFT.Player) }, null);
GetBrightnessScore = (GetBrightnessScoreMethod) methodInfo.CreateDelegate(typeof(GetBrightnessScoreMethod));
```

Personally I prefer to make it lazy and auto assign itself on get, like how I access players' active stim effects:

```csharp

internal CheckStimEffectProxy CheckEffectDelegate
{
    get
    {
        if (checkEffectDelegate == null)
        {
            var methodInfo = ReflectionHelper.FindMethodByArgTypes(typeof(EFT.HealthSystem.ActiveHealthController), new Type[] { typeof(EFT.HealthSystem.EStimulatorBuffType) }, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            checkEffectDelegate = (CheckStimEffectProxy) methodInfo.CreateDelegate(typeof(CheckStimEffectProxy), Player.ActiveHealthController);
        }
        return checkEffectDelegate;
    }
}
```