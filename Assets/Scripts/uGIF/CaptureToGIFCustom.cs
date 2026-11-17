using UnityEngine;
using System.Collections;
using System.IO;
using System.Collections.Generic;

using System.Threading;

namespace uGIF
{
	public class CaptureToGIFCustom : MonoBehaviour
	{
		public static CaptureToGIFCustom Instance;
		public List<Image> Frames = new List<Image>();
		public bool stop = false;

		[System.NonSerialized]
		public byte[] bytes = null;

        private void Awake()
        {
			Instance = this;
        }

        public IEnumerator Encode ()
		{
			bytes = null;
			stop = false;
			Error.Log(Color.yellow, "Encoding...");
			yield return new WaitForSeconds(0.1f);
			yield return _Encode();
			Error.Log(Color.yellow, "Saving gif...");
			yield return new WaitForSeconds(0.1f);
			yield return WaitForBytes();
		}

		IEnumerator WaitForBytes() {
			while(bytes == null) yield return new WaitForEndOfFrame();
			string fileName = string.Format("FateViewer_{0}", System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
			WebGLDownload.DownloadFile(bytes, fileName, "gif");
			bytes = null;
			Error.Log(Color.green, "Gif saved!");
			Frames.Clear();
			stop = false;
		}

		public IEnumerator _Encode ()
		{
			var ge = new GIFEncoder ();
			ge.useGlobalColorTable = true;
			ge.repeat = 0;
			ge.FPS = 30;
			ge.transparent = new Color32 (0, 0, 0, 0);
			ge.dispose = 2;

			var stream = new MemoryStream ();
			ge.Start (stream);
            while (!stop || Frames.Count > 0)
            {
				if(Frames[0] != null)
				{
					Frames[0].Flip();
					ge.AddFrame(Frames[0]);
					Frames.RemoveAt(0);
				}
				yield return new WaitForEndOfFrame();
            }
			ge.Finish ();
			bytes = stream.GetBuffer ();
			stream.Close ();
			yield break;
		}
	}
}