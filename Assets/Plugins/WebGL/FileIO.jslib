// Browser file download and upload for the Web build.
//
// WHY THIS EXISTS AT ALL: a browser only permits a download or a file picker from a real user
// gesture, and Unity's C# cannot originate one — the click has to be made by the page. So the
// two things the save file needs are the two things that must live in JavaScript. Everything
// else (the format, the checksum, the validation) stays in SaveCode.cs where it can be reasoned
// about, and this file is deliberately as thin as it can be.
//
// Both functions are wrapped in try/catch and log rather than throw. An exception crossing back
// into WASM takes the whole game down, and a save button that fails must not do that.

mergeInto(LibraryManager.library, {

  // Hand the player a file. The Blob is built, clicked as a link, and revoked.
  CarCrashDownload: function (namePtr, textPtr) {
    try {
      var name = UTF8ToString(namePtr);
      var text = UTF8ToString(textPtr);

      var blob = new Blob([text], { type: 'application/octet-stream' });
      var url = URL.createObjectURL(blob);

      var link = document.createElement('a');
      link.href = url;
      link.download = name;
      link.style.display = 'none';

      // Must be IN the document for the click to count in every browser.
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);

      // Not revoked immediately: some browsers start the write asynchronously and a revoked
      // URL gives an empty file. Ten seconds is far longer than any save this size needs.
      setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
    } catch (e) {
      console.error('CarCrashDownload failed:', e);
    }
  },

  // Ask for a file and post its text back to a GameObject.
  //
  // The result goes through SendMessage rather than a return value because reading a file is
  // asynchronous — FileReader fires later, long after this call has returned.
  CarCrashUpload: function (objectPtr, methodPtr) {
    try {
      var objectName = UTF8ToString(objectPtr);
      var methodName = UTF8ToString(methodPtr);

      var input = document.createElement('input');
      input.type = 'file';

      // A hint, not a restriction — every browser still offers "all files", and a save renamed
      // by a file manager must still be loadable.
      input.accept = '.crash,.txt,text/plain';
      input.style.display = 'none';

      input.onchange = function (event) {
        var file = event.target.files && event.target.files[0];

        if (!file) {
          if (input.parentNode) document.body.removeChild(input);
          return;
        }

        var reader = new FileReader();

        reader.onload = function () {
          try {
            SendMessage(objectName, methodName, String(reader.result));
          } catch (err) {
            console.error('CarCrashUpload: SendMessage failed:', err);
          }
          if (input.parentNode) document.body.removeChild(input);
        };

        reader.onerror = function () {
          console.error('CarCrashUpload: could not read the file.');
          if (input.parentNode) document.body.removeChild(input);
        };

        reader.readAsText(file);
      };

      document.body.appendChild(input);
      input.click();
    } catch (e) {
      console.error('CarCrashUpload failed:', e);
    }
  }
});
