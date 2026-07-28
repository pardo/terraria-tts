using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Terraria;

namespace ChatVoice
{
	/// <summary>
	/// P/Invoke bindings for libpiper, transcribed from libpiper/include/piper.h
	/// (piper1-gpl v1.4.2).
	///
	/// The native libraries are not packed into the .tmod — tModLoader has no
	/// mechanism for loading native code out of a mod archive. They live on disk
	/// under <see cref="DataDir"/> and are resolved explicitly.
	/// </summary>
	internal static class PiperNative
	{
		public const int PIPER_OK = 0;
		public const int PIPER_DONE = 1;
		public const int PIPER_ERR_GENERIC = -1;

		private const string LIB = "piper";

		/// <summary>Documents\My Games\Terraria\tModLoader\ChatVoice</summary>
		public static string DataDir => Path.Combine(Main.SavePath, "ChatVoice");
		public static string NativeDir => Path.Combine(DataDir, "native");
		public static string VoicesDir => Path.Combine(DataDir, "voices");
		public static string EspeakDataDir => Path.Combine(NativeDir, "espeak-ng-data");

		// CMake names the output differently across platforms and versions, so
		// probe rather than guess.
		private static readonly string[] PiperNames = {
			"piper.dll", "libpiper.dll", "libpiper.so", "libpiper.dylib",
		};

		private static readonly string[] OnnxNames = {
			"onnxruntime.dll", "libonnxruntime.so", "libonnxruntime.dylib",
			"libonnxruntime.so.1",
		};

		private static bool _resolverInstalled;
		private static IntPtr _onnxHandle;

		public static void Install()
		{
			if (_resolverInstalled)
				return;
			_resolverInstalled = true;

			NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), Resolve);
		}

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? path)
		{
			if (libraryName != LIB)
				return IntPtr.Zero;

			// onnxruntime must already be in the process before libpiper binds to
			// it. Loading by absolute path also puts its directory on the search
			// path for libpiper's own dependencies.
			if (_onnxHandle == IntPtr.Zero)
				_onnxHandle = TryLoadAny(OnnxNames);

			IntPtr handle = TryLoadAny(PiperNames);
			if (handle == IntPtr.Zero) {
				throw new DllNotFoundException(
					$"Could not load libpiper from {NativeDir}. " +
					$"Expected one of: {string.Join(", ", PiperNames)}");
			}
			return handle;
		}

		private static IntPtr TryLoadAny(string[] names)
		{
			foreach (string name in names) {
				string full = Path.Combine(NativeDir, name);
				if (!File.Exists(full))
					continue;
				if (NativeLibrary.TryLoad(full, out IntPtr handle))
					return handle;
			}
			return IntPtr.Zero;
		}

		public static bool IsInstalled()
		{
			if (!Directory.Exists(NativeDir))
				return false;
			foreach (string name in PiperNames) {
				if (File.Exists(Path.Combine(NativeDir, name)))
					return Directory.Exists(EspeakDataDir);
			}
			return false;
		}

		// ------------------------------------------------------------------
		// structs
		// ------------------------------------------------------------------

		[StructLayout(LayoutKind.Sequential)]
		public struct PiperAudioChunk
		{
			public IntPtr Samples;        // const float*
			public UIntPtr NumSamples;    // size_t
			public int SampleRate;
			[MarshalAs(UnmanagedType.I1)]
			public bool IsLast;           // C99 bool, one byte

			public IntPtr Phonemes;       // const char32_t*
			public UIntPtr NumPhonemes;
			public IntPtr PhonemeIds;     // const int*
			public UIntPtr NumPhonemeIds;
			public IntPtr Alignments;     // const int*
			public UIntPtr NumAlignments;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct PiperSynthesizeOptions
		{
			public int SpeakerId;
			public float LengthScale;
			public float NoiseScale;
			public float NoiseWScale;
		}

		// ------------------------------------------------------------------
		// functions
		// ------------------------------------------------------------------

		[DllImport(LIB, EntryPoint = "piper_create", CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr Create(
			[MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string configPath,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string espeakDataPath);

		[DllImport(LIB, EntryPoint = "piper_free", CallingConvention = CallingConvention.Cdecl)]
		public static extern void Free(IntPtr synth);

		[DllImport(LIB, EntryPoint = "piper_default_synthesize_options", CallingConvention = CallingConvention.Cdecl)]
		public static extern PiperSynthesizeOptions DefaultOptions(IntPtr synth);

		[DllImport(LIB, EntryPoint = "piper_synthesize_start", CallingConvention = CallingConvention.Cdecl)]
		public static extern int SynthesizeStart(
			IntPtr synth,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string text,
			ref PiperSynthesizeOptions options);

		[DllImport(LIB, EntryPoint = "piper_synthesize_next", CallingConvention = CallingConvention.Cdecl)]
		public static extern int SynthesizeNext(IntPtr synth, out PiperAudioChunk chunk);

		[DllImport(LIB, EntryPoint = "piper_version", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr VersionPtr();

		public static string Version()
		{
			try {
				IntPtr p = VersionPtr();
				return p == IntPtr.Zero ? "unknown" : Marshal.PtrToStringUTF8(p);
			}
			catch {
				return "unknown";
			}
		}
	}
}
