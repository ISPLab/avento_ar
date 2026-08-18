#import <Foundation/Foundation.h>

/// Native callbacks required by AventoUnityNative.cs (`DllImport("__Internal")`).
/// These must live inside UnityFramework (UaaL). The Capacitor host observes notifications.

static NSString *AventoUnityNSString(const char *json)
{
    if (json == NULL)
        return @"{}";
    return [NSString stringWithUTF8String:json] ?: @"{}";
}

static void AventoUnityPost(NSString *name, const char *json)
{
    NSString *payload = AventoUnityNSString(json);
    dispatch_async(dispatch_get_main_queue(), ^{
        [[NSNotificationCenter defaultCenter] postNotificationName:name
                                                            object:nil
                                                          userInfo:@{@"json": payload}];
    });
}

#ifdef __cplusplus
extern "C" {
#endif

void AventoUnity_OnReady(const char *json)
{
    AventoUnityPost(@"AventoUnityOnReady", json);
}

void AventoUnity_OnSessionEnded(const char *json)
{
    AventoUnityPost(@"AventoUnityOnSessionEnded", json);
}

void AventoUnity_OnError(const char *json)
{
    AventoUnityPost(@"AventoUnityOnError", json);
}

void AventoUnity_OnObjectInteract(const char *json)
{
    AventoUnityPost(@"AventoUnityOnObjectInteract", json);
}

#ifdef __cplusplus
}
#endif
