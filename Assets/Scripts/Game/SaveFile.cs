using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Progress as a downloadable <c>.crash</c> file, and back again.
/// </summary>
/// <remarks>
/// **The file is the save CODE in a wrapper — no second format.** `SaveCode` already owns the
/// encoding, the checksum and the validation, and it has already shipped one format bug (the
/// nested delimiter). A second serialiser would be a second thing to get wrong and a second thing
/// to keep in step. This adds a way to MOVE that string, nothing more.
///
/// **Why any of this needs JavaScript.** A browser only permits a download or a file picker from
/// a real user gesture, and Unity's C# cannot originate one — the click has to be made by the
/// page. So the two things that must live in `Plugins/WebGL/FileIO.jslib` are exactly those two,
/// and everything else stays here.
///
/// **Why it is worth having on top of the text code.** The code is immune to blocked storage; the
/// file is immune to blocked storage AND to a mis-paste, which is the failure a 40-character
/// string actually has on a Chromebook with no clipboard manager. It also survives being emailed
/// to yourself, which is how a save gets from the school machine to a home one.
///
/// Outside the Web build there is no browser, so the Editor writes to
/// <see cref="Application.persistentDataPath"/> and says where — enough to test the round trip.
/// </remarks>
public static class SaveFile
{
    /// <summary>Suggested filename. The extension is cosmetic; the content is the save code.</summary>
    public const string FileName = "carcrash-progress.crash";

    /// <summary>True where a real download and file picker are available.</summary>
    public static bool Supported =>
#if UNITY_WEBGL && !UNITY_EDITOR
        true;
#else
        false;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void CarCrashDownload(string fileName, string text);

    [DllImport("__Internal")]
    static extern void CarCrashUpload(string objectName, string methodName);
#endif

    /// <summary>Hand the player their progress as a file.</summary>
    public static void Save()
    {
        string code = SaveCode.Export();

#if UNITY_WEBGL && !UNITY_EDITOR
        CarCrashDownload(FileName, code);
#else
        string path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
        try
        {
            System.IO.File.WriteAllText(path, code);
            Debug.Log($"SaveFile: no browser here, so the save was written to {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"SaveFile: could not write {path} — {e.Message}");
        }
#endif
    }

    /// <summary>
    /// Ask for a file and apply it. <paramref name="onResult"/> gets the message to show.
    /// </summary>
    /// <remarks>
    /// The callback is stored rather than passed through, because reading a file is asynchronous:
    /// the picker returns immediately and `FileReader` fires later. The result comes back through
    /// `SendMessage` to a receiver GameObject, which is the only channel JavaScript has into a
    /// Unity build.
    ///
    /// The receiver is created on demand and marked `DontDestroyOnLoad` — the menu scene can
    /// unload while a file dialog is open, and a `SendMessage` to a destroyed object is a silent
    /// no-op, which would look exactly like the player having cancelled.
    /// </remarks>
    public static void Load(Action<bool, string> onResult)
    {
        pending = onResult;

#if UNITY_WEBGL && !UNITY_EDITOR
        Receiver().Arm();
        CarCrashUpload(ReceiverName, nameof(SaveFileReceiver.OnFileLoaded));
#else
        string path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
        try
        {
            Deliver(System.IO.File.ReadAllText(path));
        }
        catch (Exception e)
        {
            Deliver("");
            Debug.LogWarning($"SaveFile: no browser here, and {path} could not be read — " +
                             e.Message);
        }
#endif
    }

    const string ReceiverName = "CarCrashSaveFileReceiver";

    static Action<bool, string> pending;
    static SaveFileReceiver receiver;

    static SaveFileReceiver Receiver()
    {
        if (receiver != null) return receiver;

        GameObject go = new GameObject(ReceiverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        receiver = go.AddComponent<SaveFileReceiver>();
        return receiver;
    }

    /// <summary>Apply file text. Called from the receiver, or directly outside the Web build.</summary>
    internal static void Deliver(string text)
    {
        Action<bool, string> callback = pending;
        pending = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            callback?.Invoke(false, "That file is empty.");
            return;
        }

        // TryImport validates fully before touching the wallet, so a wrong file — a screenshot
        // renamed, someone else's homework — changes nothing.
        bool ok = SaveCode.TryImport(text, out string message);
        callback?.Invoke(ok, message);
    }
}

/// <summary>
/// The GameObject JavaScript can `SendMessage` to. Exists only because that call addresses a
/// GameObject BY NAME and needs a MonoBehaviour method to land on.
/// </summary>
public class SaveFileReceiver : MonoBehaviour
{
    /// <summary>Kept so the object is not mistaken for stray scene junk.</summary>
    public void Arm() { }

    /// <summary>Called from FileIO.jslib with the file's text.</summary>
    public void OnFileLoaded(string text) => SaveFile.Deliver(text);
}
