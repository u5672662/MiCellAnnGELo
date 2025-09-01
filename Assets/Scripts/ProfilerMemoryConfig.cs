using UnityEngine;
using UnityEngine.Profiling;

public static class ProfilerMemoryConfig
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void SetProfilerMemory()
    {
        Profiler.maxUsedMemory = 256 * 1024 * 1024;
    }
}