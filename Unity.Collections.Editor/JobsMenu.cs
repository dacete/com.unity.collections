using System;
using UnityEditor;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

class JobsMenu
{
    private static int savedJobWorkerCount = JobsUtility.JobWorkerCount;

    [SettingsProvider]
    private static SettingsProvider JobsPreferencesItem()
    {
        var provider = new SettingsProvider("Preferences/Jobs", SettingsScope.User)
        {
            label = "Jobs",
            keywords = new[]{"Jobs"},
            guiHandler = (searchContext) =>
            {
                var originalWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 200f;
                EditorGUILayout.BeginVertical();

                GUILayout.BeginVertical();
                EditorGUILayout.LabelField("For safety, these values are reset on editor restart.");
                bool oldval = (JobsUtility.JobWorkerCount > 0);
                 
                bool newval = EditorGUILayout.Toggle(new GUIContent("Use Job Threads:"), oldval);
                if (newval != oldval)
                    SwitchUseJobThreads();

                JobsUtility.JobDebuggerEnabled = EditorGUILayout.Toggle(new GUIContent("Enable Jobs Debugger"),
                    JobsUtility.JobDebuggerEnabled);

                var previousMode = NativeLeakDetection.Mode;

                var newMode =
                    (NativeLeakDetectionMode)EditorGUILayout.EnumPopup(new GUIContent("Leak Detection Level"),
                        previousMode);
                if (newMode != previousMode)
                {
                    switch (newMode)
                    {
                        case NativeLeakDetectionMode.Disabled:
                            SwitchLeaksOff();
                            break;
                        case NativeLeakDetectionMode.Enabled:
                            SwitchLeaksOn();
                            break;
                        case NativeLeakDetectionMode.EnabledWithStackTrace:
                            SwitchLeaksFull();
                            break;
                    }
                }
                GUILayout.EndVertical();
                EditorGUILayout.EndVertical();

                EditorGUIUtility.labelWidth = originalWidth;
            }

        };
        return provider;
    }

    static void SwitchUseJobThreads()
    {
        if (JobsUtility.JobWorkerCount > 0)
        {
            savedJobWorkerCount = JobsUtility.JobWorkerCount;
            try
            {
                JobsUtility.JobWorkerCount = 0;
            }
            catch (System.ArgumentOutOfRangeException e) when (e.ParamName == "JobWorkerCount")
            {
                Debug.LogWarning("Disabling Job Threads requires Unity Version 2020.1.a15 or newer");
            }
        }
        else
        {
            JobsUtility.JobWorkerCount = savedJobWorkerCount;
            if (savedJobWorkerCount == 0)
            {
                JobsUtility.ResetJobWorkerCount();
            }
        }
    }

    static void SwitchLeaksOff()
    {
        // In the case where someone enables, disables, then re-enables leak checking, we might miss some frees
        // while disabled. So to avoid spurious leak warnings, just forgive the leaks every time someone disables
        // leak checking through the menu.
        UnsafeUtility.ForgiveLeaks();
        NativeLeakDetection.Mode = NativeLeakDetectionMode.Disabled;
        Debug.LogWarning("Leak detection has been disabled. Leak warnings will not be generated, and all leaks up to now are forgotten.");
    }

    static void SwitchLeaksOn()
    {
        NativeLeakDetection.Mode = NativeLeakDetectionMode.Enabled;
        Debug.Log("Leak detection has been enabled. Leak warnings will be generated upon exiting play mode.");
    }

    static void SwitchLeaksFull()
    {
        NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
        Debug.Log("Leak detection with stack traces has been enabled. Leak warnings will be generated upon exiting play mode.");
    }
}
