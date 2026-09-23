using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PocketRoles.Net
{
    // v0.5.5 self-scan, native part (2026-09-22 request, approved "足しちゃおう"): (1) the executable sections of
    // GameAssembly.dll in memory against the same bytes of the file on disk, and (2) the names of the loaded kernel drivers.
    // Plain P/Invoke and PE parsing, no BepInEx / Unity / Il2CppInterop, written in C# 5 on purpose: the scratch tests compile
    // this very file with Windows PowerShell 5.1 Add-Type in a 32-bit process against a copy of the real GameAssembly.dll.
    // What is allowed and what is reported (BepInEx / Harmony hook sites, BLOCK or notice) is SelfScan.cs's job.

    /// <summary>One modified place of the code: consecutive differing bytes (up to <see cref="CodeImage.MergeGap"/> equal bytes inside).</summary>
    internal sealed class CodeRegion
    {
        public readonly int Rva;
        /// <summary>Expected bytes (the file + base relocations); null for an unreadable page.</summary>
        public readonly byte[] Orig;
        /// <summary>Bytes in memory; null for an unreadable page (guard / no-access / not committed).</summary>
        public readonly byte[] Now;

        public CodeRegion(int rva, byte[] orig, byte[] now) { Rva = rva; Orig = orig; Now = now; }

        public bool Unreadable { get { return Now == null; } }
        public int Length { get { return Now == null ? CodeImage.PageSize : Now.Length; } }
        public int End { get { return Rva + Length; } }

        public bool SameBytes(byte[] other)
        {
            if (Now == null || other == null) return Now == null && other == null;
            if (other.Length != Now.Length) return false;
            for (int i = 0; i < Now.Length; i++) if (Now[i] != other[i]) return false;
            return true;
        }
    }

    /// <summary>
    /// The loaded GameAssembly.dll (or any DLL) and its file. The expected code is rebuilt from the file: the raw bytes of
    /// every executable section (IL2CPP games: ".text" = libil2cpp + CRT, "il2cpp" = the game's code) with the base
    /// relocations applied for the actual load address (a 32-bit IL2CPP GameAssembly.dll is ASLR-relocated, and its code is
    /// full of absolute addresses). Bytes the loader writes (IAT, delay-load IATs, the security cookie, CFG pointers, and
    /// relocation kinds this class does not rebuild) are masked if they ever lie in an executable section. Memory is read
    /// with ReadProcessMemory on this process (an unreadable page never throws) after VirtualQuery, so a guard page is
    /// reported, not touched. Not thread-safe: one scanner thread.
    /// </summary>
    internal sealed class CodeImage
    {
        public const int PageSize = 4096;
        /// <summary>Equal bytes allowed inside one modified place (a jump whose displacement happens to match the original).</summary>
        public const int MergeGap = 4;
        private const int Chunk = 16 * PageSize;
        private const ulong UnreadableHash = 1UL;

        private sealed class Section
        {
            public string Name;
            public int Rva, Size, RawPtr, RawSize, FirstPage, Pages;
            public bool Exec;
        }

        public readonly IntPtr Base;
        public readonly string FilePath;
        public bool Is64 { get; private set; }
        /// <summary>Load address minus the file's ImageBase (0 = not relocated).</summary>
        public long Delta { get; private set; }
        public int SizeOfImage { get; private set; }
        /// <summary>Pages of all executable sections (the hash array's length).</summary>
        public int PageCount { get; private set; }
        public long CodeBytes { get; private set; }
        /// <summary>Base relocations applied (types 1/2/3/10); others are masked (<see cref="MaskedRelocs"/>).</summary>
        public int RelocCount { get; private set; }
        public int MaskedRelocs { get; private set; }

        private readonly List<Section> _all = new List<Section>();
        private readonly List<Section> _exec = new List<Section>();
        private byte[] _reloc;
        private int[] _blkPage, _blkOff, _blkCnt;
        private int[] _maskStart = new int[0], _maskEnd = new int[0];
        private long _fileLength, _fileTicks;
        private long _fileImageBase;
        private FileStream _fs;
        private readonly byte[] _mem = new byte[Chunk], _pri = new byte[Chunk];
        private byte[] _tmp = new byte[Chunk + 64];

        private CodeImage(IntPtr moduleBase, string path)
        {
            Base = moduleBase;
            FilePath = path;
        }

        /// <summary>Parses the file and checks that it is the loaded module. Null (and <paramref name="error"/>) when it cannot be used.</summary>
        public static CodeImage Open(IntPtr moduleBase, string path, out string error)
        {
            error = null;
            CodeImage img = null;
            try
            {
                if (moduleBase == IntPtr.Zero) { error = "module not loaded"; return null; }
                img = new CodeImage(moduleBase, path);
                error = img.Load();
                if (error == null) return img;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
            }
            if (img != null) img.Close();
            return null;
        }

        /// <summary>Releases the file handle (the check is over).</summary>
        public void Close()
        {
            try { if (_fs != null) _fs.Dispose(); } catch (Exception) { }
            _fs = null;
        }

        private string Load()
        {
            var fi = new FileInfo(FilePath);
            if (!fi.Exists) return "file not found";
            _fileLength = fi.Length;
            _fileTicks = fi.LastWriteTimeUtc.Ticks;
            _fs = OpenSessionFile();
            var fs = _fs;
            {
                var h = new byte[PageSize];
                int n = ReadAt(fs, 0, h, 0, h.Length);
                if (n < 512 || U16(h, 0) != 0x5A4D) return "not a PE file";
                int pe = I32(h, 0x3C);
                if (pe <= 0 || pe > n - 256 || U32(h, pe) != 0x00004550) return "no PE header";
                int nsec = U16(h, pe + 6), optSize = U16(h, pe + 20), opt = pe + 24;
                int magic = U16(h, opt);
                if (magic != 0x10B && magic != 0x20B) return "unknown optional header";
                Is64 = magic == 0x20B;
                if (Is64 != (IntPtr.Size == 8)) return "the file's bitness differs from this process";
                uint stamp = U32(h, pe + 8);
                _fileImageBase = Is64 ? (long)U64(h, opt + 24) : (long)U32(h, opt + 28);
                SizeOfImage = I32(h, opt + 56);
                int ndd = I32(h, opt + (Is64 ? 108 : 92)), dd = opt + (Is64 ? 112 : 96);
                int secTable = opt + optSize;
                if (nsec <= 0 || nsec > 96 || secTable + nsec * 40 > n) return "section table not readable";

                // The loaded module must be this file (same time stamp and size in its in-memory headers).
                var mh = new byte[secTable + nsec * 40];
                if (!ReadMemory(0, mh, mh.Length)) return "the loaded module's headers cannot be read";
                if (U32(mh, pe + 8) != stamp || I32(mh, opt + 56) != SizeOfImage) return "the file on disk is not the loaded module";

                int page = 0;
                for (int i = 0; i < nsec; i++)
                {
                    int o = secTable + i * 40;
                    var s = new Section
                    {
                        Name = Encoding.ASCII.GetString(h, o, 8).TrimEnd('\0'),
                        Size = I32(h, o + 8), Rva = I32(h, o + 12), RawSize = I32(h, o + 16), RawPtr = I32(h, o + 20),
                    };
                    uint ch = U32(h, o + 36);
                    if (s.Size == 0) s.Size = s.RawSize;
                    s.Exec = (ch & 0x20000000u) != 0 || (ch & 0x20u) != 0;   // MEM_EXECUTE or CNT_CODE
                    _all.Add(s);
                    if (!s.Exec || s.Size <= 0) continue;
                    if ((s.Rva & (PageSize - 1)) != 0) return "executable section " + s.Name + " is not page aligned";
                    s.FirstPage = page;
                    s.Pages = (s.Size + PageSize - 1) / PageSize;
                    page += s.Pages;
                    CodeBytes += s.Size;
                    _exec.Add(s);
                }
                if (_exec.Count == 0) return "no executable section";
                PageCount = page;
                Delta = Base.ToInt64() - _fileImageBase;

                var masks = new List<KeyValuePair<int, int>>();
                int relRva = ndd > 5 ? I32(h, dd + 40) : 0, relSize = ndd > 5 ? I32(h, dd + 44) : 0;
                if (relSize > 0) { string e = LoadRelocs(fs, relRva, relSize, masks); if (e != null) return e; }
                else if (Delta != 0) return "relocated but without a relocation table";

                if (ndd > 12) AddMask(masks, I32(h, dd + 96), I32(h, dd + 100));                        // IAT
                if (ndd > 13) MaskDelayImports(fs, I32(h, dd + 104), I32(h, dd + 108), masks);           // delay-load IATs
                if (ndd > 10) MaskLoadConfig(fs, I32(h, dd + 80), I32(h, dd + 84), masks);               // cookie, CFG pointers
                BuildMasks(masks);
            }
            return null;
        }

        private string LoadRelocs(FileStream fs, int rva, int size, List<KeyValuePair<int, int>> masks)
        {
            long off = RvaToOffset(rva);
            if (off < 0) return "relocation table not in the file";
            _reloc = new byte[size];
            if (ReadAt(fs, off, _reloc, 0, size) != size) return "relocation table truncated";
            var pages = new List<int>(); var offs = new List<int>(); var cnts = new List<int>();
            int p = 0, applied = 0, masked = 0;
            bool sorted = true;
            while (p + 8 <= size)
            {
                int pg = I32(_reloc, p), bs = I32(_reloc, p + 4);
                if (bs == 0 && pg == 0) break;
                if (bs < 8 || p + bs > size) return "bad relocation block at +" + p;
                int cnt = (bs - 8) / 2;
                if (pages.Count > 0 && pg < pages[pages.Count - 1]) sorted = false;
                pages.Add(pg); offs.Add(p + 8); cnts.Add(cnt);
                for (int k = 0; k < cnt; k++)
                {
                    int e = U16(_reloc, p + 8 + 2 * k), type = e >> 12;
                    if (type == 0) continue;
                    if (type == 1 || type == 2 || type == 3 || type == 10) { applied++; continue; }
                    // HIGHADJ / MIPS / ARM kinds: never seen in an x86 / x64 MSVC DLL; not rebuilt, masked instead
                    masked++;
                    AddMask(masks, pg + (e & 0xFFF), 8);
                    if (type == 4) k++;   // HIGHADJ uses the next slot
                }
                p += bs;
            }
            _blkPage = pages.ToArray(); _blkOff = offs.ToArray(); _blkCnt = cnts.ToArray();
            if (!sorted)
            {
                var idx = new int[_blkPage.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                var keys = (int[])_blkPage.Clone();
                Array.Sort(keys, idx);
                var o2 = new int[idx.Length]; var c2 = new int[idx.Length];
                for (int i = 0; i < idx.Length; i++) { o2[i] = _blkOff[idx[i]]; c2[i] = _blkCnt[idx[i]]; }
                _blkPage = keys; _blkOff = o2; _blkCnt = c2;
            }
            RelocCount = applied;
            MaskedRelocs = masked;
            return null;
        }

        private void MaskDelayImports(FileStream fs, int rva, int size, List<KeyValuePair<int, int>> masks)
        {
            if (rva <= 0 || size < 32) return;
            long off = RvaToOffset(rva);
            if (off < 0) return;
            var d = new byte[Math.Min(size, 32 * 256)];
            int n = ReadAt(fs, off, d, 0, d.Length);
            int ptr = Is64 ? 8 : 4;
            for (int o = 0; o + 32 <= n; o += 32)
            {
                uint attr = U32(d, o);
                long hmod = U32(d, o + 8), iat = U32(d, o + 12), intab = U32(d, o + 16);
                if (hmod == 0 && iat == 0 && intab == 0) break;
                if ((attr & 1) == 0) { hmod -= _fileImageBase; iat -= _fileImageBase; intab -= _fileImageBase; }   // old VA form
                int entries = CountThunks(fs, (int)intab, ptr);
                AddMask(masks, (int)hmod, ptr);
                AddMask(masks, (int)iat, (entries + 1) * ptr);
            }
        }

        private int CountThunks(FileStream fs, int rva, int ptr)
        {
            long off = RvaToOffset(rva);
            if (off < 0) return 64;   // unknown: mask a generous block
            var b = new byte[ptr * 512];
            int n = ReadAt(fs, off, b, 0, b.Length), c = 0;
            for (int o = 0; o + ptr <= n; o += ptr, c++)
                if ((ptr == 8 ? U64(b, o) : U32(b, o)) == 0) return c;
            return c;
        }

        private void MaskLoadConfig(FileStream fs, int rva, int size, List<KeyValuePair<int, int>> masks)
        {
            if (rva <= 0 || size < 4) return;
            long off = RvaToOffset(rva);
            if (off < 0) return;
            var b = new byte[Math.Min(size, 512)];
            int n = ReadAt(fs, off, b, 0, b.Length);
            int structSize = Math.Min(n, (int)U32(b, 0));
            int ptr = Is64 ? 8 : 4;
            int[] fields = Is64 ? new[] { 0x58, 0x70, 0x78 } : new[] { 0x3C, 0x48, 0x4C };   // SecurityCookie, GuardCFCheck/DispatchFunctionPointer
            foreach (int f in fields)
            {
                if (f + ptr > structSize) continue;
                long va = ptr == 8 ? (long)U64(b, f) : (long)U32(b, f);
                if (va != 0) AddMask(masks, (int)(va - _fileImageBase), ptr);
            }
        }

        private void AddMask(List<KeyValuePair<int, int>> masks, int rva, int len)
        {
            if (rva <= 0 || len <= 0) return;
            foreach (var s in _exec)
                if (rva < s.Rva + s.Size && rva + len > s.Rva) { masks.Add(new KeyValuePair<int, int>(rva, rva + len)); return; }
        }

        private void BuildMasks(List<KeyValuePair<int, int>> masks)
        {
            masks.Sort((a, b) => a.Key.CompareTo(b.Key));
            _maskStart = new int[masks.Count]; _maskEnd = new int[masks.Count];
            for (int i = 0; i < masks.Count; i++) { _maskStart[i] = masks[i].Key; _maskEnd[i] = masks[i].Value; }
        }

        /// <summary>Bytes the loader writes: the expected bytes take the memory's value there.</summary>
        private void Mask(int rva, byte[] pri, byte[] mem, int len)
        {
            for (int i = 0; i < _maskStart.Length; i++)
            {
                int a = Math.Max(rva, _maskStart[i]), b = Math.Min(rva + len, _maskEnd[i]);
                if (a < b) Buffer.BlockCopy(mem, a - rva, pri, a - rva, b - a);
            }
        }

        public int MaskCount { get { return _maskStart.Length; } }

        // ------------------------------------------------------------------------------------------ file side

        /// <summary>
        /// One read handle for the whole session. It does not share write or delete, so while the game runs the file cannot be
        /// renamed and replaced by another one (the loader's mapping already refuses writes); when that open is refused (another
        /// program holds the file with delete access), a sharing handle is used and <see cref="Locked"/> is false.
        /// </summary>
        private FileStream OpenSessionFile()
        {
            try
            {
                var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
                Locked = true;
                return fs;
            }
            catch (IOException)
            {
                Locked = false;
                return new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.RandomAccess);
            }
        }

        /// <summary>The session handle keeps the file from being renamed or replaced (see <see cref="OpenSessionFile"/>).</summary>
        public bool Locked { get; private set; }

        /// <summary>The file on disk still has the size and time stamp it had when it was opened.</summary>
        public bool FileUnchanged()
        {
            try
            {
                var fi = new FileInfo(FilePath);
                return fi.Exists && fi.Length == _fileLength && fi.LastWriteTimeUtc.Ticks == _fileTicks;
            }
            catch (Exception) { return false; }
        }

        private static int ReadAt(FileStream fs, long off, byte[] buf, int at, int len)
        {
            fs.Position = off;
            int got = 0;
            while (got < len)
            {
                int r = fs.Read(buf, at + got, len - got);
                if (r <= 0) break;
                got += r;
            }
            return got;
        }

        private long RvaToOffset(int rva)
        {
            foreach (var s in _all)
            {
                int span = Math.Max(s.Size, s.RawSize);
                if (rva >= s.Rva && rva < s.Rva + span)
                    return rva - s.Rva < s.RawSize ? (long)s.RawPtr + (rva - s.Rva) : -1;
            }
            return -1;
        }

        /// <summary>The expected bytes of [rva, rva+len) (inside one executable section): file bytes with relocations applied.</summary>
        private void ReadPristine(FileStream fs, Section s, int rva, byte[] buf, int len)
        {
            // 8 bytes of margin on both sides, so a fixup that overlaps the range is always complete in the work buffer
            int outerStart = Math.Max(s.Rva, rva - 8), outerEnd = Math.Min(s.Rva + s.Size, rva + len + 8);
            int n = outerEnd - outerStart;
            if (_tmp.Length < n) _tmp = new byte[n];
            Array.Clear(_tmp, 0, n);
            int secOff = outerStart - s.Rva, avail = Math.Min(n, s.RawSize - secOff);
            if (avail > 0 && ReadAt(fs, (long)s.RawPtr + secOff, _tmp, 0, avail) != avail) throw new IOException("GameAssembly.dll could not be read to the end");
            ApplyRelocs(_tmp, outerStart, n);
            Buffer.BlockCopy(_tmp, rva - outerStart, buf, 0, len);
        }

        private void ApplyRelocs(byte[] b, int bRva, int n)
        {
            if (Delta == 0 || _blkPage == null) return;
            int first = bRva & ~(PageSize - 1), last = (bRva + n - 1) & ~(PageSize - 1);
            int lo = 0, hi = _blkPage.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (_blkPage[mid] < first) lo = mid + 1; else hi = mid; }
            uint d32 = unchecked((uint)Delta);
            ulong d64 = unchecked((ulong)Delta);
            for (int i = lo; i < _blkPage.Length && _blkPage[i] <= last; i++)
            {
                int page = _blkPage[i], o = _blkOff[i], c = _blkCnt[i];
                for (int k = 0; k < c; k++)
                {
                    int e = _reloc[o + 2 * k] | (_reloc[o + 2 * k + 1] << 8), type = e >> 12;
                    int at = page + (e & 0xFFF) - bRva;
                    switch (type)
                    {
                        case 3:   // HIGHLOW
                            if (at >= 0 && at + 4 <= n) W32(b, at, unchecked(U32(b, at) + d32));
                            break;
                        case 10:  // DIR64
                            if (at >= 0 && at + 8 <= n) W64(b, at, unchecked(U64(b, at) + d64));
                            break;
                        case 1:   // HIGH
                            if (at >= 0 && at + 2 <= n) W16(b, at, unchecked((ushort)(U16(b, at) + (ushort)(d32 >> 16))));
                            break;
                        case 2:   // LOW
                            if (at >= 0 && at + 2 <= n) W16(b, at, unchecked((ushort)(U16(b, at) + (ushort)(d32 & 0xFFFF))));
                            break;
                        case 4:   // HIGHADJ (masked, see LoadRelocs): skip its second slot
                            k++;
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------ memory side

        [StructLayout(LayoutKind.Sequential)]
        private struct Mbi
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr read);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualQuery(IntPtr address, out Mbi info, IntPtr length);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        private static readonly IntPtr Self = new IntPtr(-1);

        /// <summary>A module of this process by file name, with its full path (IntPtr.Zero when not loaded).</summary>
        public static IntPtr FindModule(string name, out string path)
        {
            path = null;
            IntPtr h = GetModuleHandleW(name);
            if (h == IntPtr.Zero) return IntPtr.Zero;
            path = ModulePath(h);
            return h;
        }

        /// <summary>Address of an export (0 when missing).</summary>
        public static long Export(IntPtr module, string name)
        {
            try { return module == IntPtr.Zero ? 0 : GetProcAddress(module, name).ToInt64(); }
            catch (Exception) { return 0; }
        }

        private static string ModulePath(IntPtr h)
        {
            var sb = new StringBuilder(32768);
            uint n = GetModuleFileNameW(h, sb, (uint)sb.Capacity);
            if (n == 0) return null;
            string p = sb.ToString(0, (int)n);
            if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
            return p;
        }

        private IntPtr At(long rva) { return new IntPtr(Base.ToInt64() + rva); }

        private bool ReadMemory(int rva, byte[] buf, int len)
        {
            IntPtr got;
            return ReadProcessMemory(Self, At(rva), buf, new IntPtr(len), out got) && got.ToInt64() == len;
        }

        private static bool ReadAbs(long address, byte[] buf, int len)
        {
            IntPtr got;
            return ReadProcessMemory(Self, new IntPtr(address), buf, new IntPtr(len), out got) && got.ToInt64() == len;
        }

        private static bool Readable(Mbi m)
        {
            const uint MEM_COMMIT = 0x1000, PAGE_NOACCESS = 0x01, PAGE_GUARD = 0x100;
            return m.State == MEM_COMMIT && m.Protect != 0 && (m.Protect & (PAGE_NOACCESS | PAGE_GUARD)) == 0;
        }

        /// <summary>Readable pages of [rva, rva+len) (page granularity, <paramref name="rva"/> page aligned).</summary>
        private bool[] ReadablePages(int rva, int len)
        {
            int pages = (len + PageSize - 1) / PageSize;
            var ok = new bool[pages];
            long start = Base.ToInt64() + rva, end = start + len, addr = start;
            int mbiSize = Marshal.SizeOf(typeof(Mbi));
            while (addr < end)
            {
                Mbi m;
                if (VirtualQuery(new IntPtr(addr), out m, new IntPtr(mbiSize)) == IntPtr.Zero) break;
                long rs = m.BaseAddress.ToInt64(), re = rs + m.RegionSize.ToInt64();
                if (re <= addr) break;
                if (Readable(m))
                {
                    int a = (int)Math.Max(0, (Math.Max(rs, start) - start) / PageSize);
                    int b = (int)Math.Min(pages, (Math.Min(re, end) - start + PageSize - 1) / PageSize);
                    for (int i = a; i < b; i++) ok[i] = true;
                }
                addr = re;
            }
            return ok;
        }

        /// <summary>
        /// Reads [rva, rva+len) (page aligned, at most <see cref="Chunk"/>) into <see cref="_mem"/>; <paramref name="okPages"/>
        /// says which pages were read (the others are left as they were and must not be compared).
        /// </summary>
        private void ReadChunk(int rva, int len, bool[] readable, int firstIdx, bool[] okPages)
        {
            int pages = (len + PageSize - 1) / PageSize;
            bool all = true;
            for (int i = 0; i < pages; i++) if (!readable[firstIdx + i]) { all = false; break; }
            if (all && ReadMemory(rva, _mem, len))
            {
                for (int i = 0; i < pages; i++) okPages[i] = true;
                return;
            }
            var one = new byte[PageSize];
            for (int i = 0; i < pages; i++)
            {
                okPages[i] = false;
                if (!readable[firstIdx + i]) continue;
                int pl = Math.Min(PageSize, len - i * PageSize);
                if (ReadMemory(rva + i * PageSize, one, pl)) { Buffer.BlockCopy(one, 0, _mem, i * PageSize, pl); okPages[i] = true; }
            }
        }

        private static ulong Hash(byte[] b, int off, int len)
        {
            ulong h = 14695981039346656037UL ^ (ulong)len;
            int i = 0;
            for (; i + 8 <= len; i += 8)
            {
                h ^= BitConverter.ToUInt64(b, off + i);
                h = unchecked(h * 1099511628211UL);
                h ^= h >> 29;
            }
            for (; i < len; i++) { h ^= b[off + i]; h = unchecked(h * 1099511628211UL); }
            return h == UnreadableHash ? 2UL : h;
        }

        /// <summary>Hash of every executable page as it is in memory now (unreadable pages get a fixed value).</summary>
        public void HashAll(ulong[] hashes)
        {
            var okPages = new bool[Chunk / PageSize];
            foreach (var s in _exec)
            {
                bool[] readable = ReadablePages(s.Rva, s.Size);
                for (int c = 0; c < s.Size; c += Chunk)
                {
                    int len = Math.Min(Chunk, s.Size - c);
                    ReadChunk(s.Rva + c, len, readable, c / PageSize, okPages);
                    for (int i = 0; i * PageSize < len; i++)
                    {
                        int pl = Math.Min(PageSize, len - i * PageSize);
                        hashes[s.FirstPage + c / PageSize + i] = okPages[i] ? Hash(_mem, i * PageSize, pl) : UnreadableHash;
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------ comparison

        /// <summary>Collects modified places from a stream of (expected, memory) bytes.</summary>
        private sealed class Collector
        {
            public readonly List<CodeRegion> Out = new List<CodeRegion>();
            private int _start = -1, _gap;
            private readonly List<byte> _o = new List<byte>(), _n = new List<byte>();
            private readonly byte[] _gapBytes = new byte[MergeGap];

            public bool Open { get { return _start >= 0; } }

            public void Feed(int rva, byte[] pri, byte[] mem, int off, int len)
            {
                for (int i = 0; i < len; i++)
                {
                    byte p = pri[off + i], m = mem[off + i];
                    if (p != m)
                    {
                        if (_start < 0) _start = rva + i;
                        else for (int g = 0; g < _gap; g++) { _o.Add(_gapBytes[g]); _n.Add(_gapBytes[g]); }
                        _gap = 0;
                        _o.Add(p); _n.Add(m);
                    }
                    else if (_start >= 0)
                    {
                        if (_gap == MergeGap) Close();
                        else _gapBytes[_gap++] = m;
                    }
                }
            }

            public void Close()
            {
                if (_start >= 0) Out.Add(new CodeRegion(_start, _o.ToArray(), _n.ToArray()));
                _start = -1; _gap = 0; _o.Clear(); _n.Clear();
            }

            public void Unreadable(int pageRva)
            {
                Close();
                Out.Add(new CodeRegion(pageRva, null, null));
            }
        }

        /// <summary>
        /// Every modified place of every executable section, and (when <paramref name="hashes"/> is given) the page hashes of
        /// the memory as read in the same pass.
        /// </summary>
        public List<CodeRegion> FullDiff(ulong[] hashes)
        {
            var col = new Collector();
            var okPages = new bool[Chunk / PageSize];
            var fs = _fs;
            {
                foreach (var s in _exec)
                {
                    bool[] readable = ReadablePages(s.Rva, s.Size);
                    for (int c = 0; c < s.Size; c += Chunk)
                    {
                        int len = Math.Min(Chunk, s.Size - c), rva = s.Rva + c;
                        ReadChunk(rva, len, readable, c / PageSize, okPages);
                        ReadPristine(fs, s, rva, _pri, len);
                        Mask(rva, _pri, _mem, len);
                        for (int i = 0; i * PageSize < len; i++)
                        {
                            int pl = Math.Min(PageSize, len - i * PageSize), pr = rva + i * PageSize;
                            if (!okPages[i])
                            {
                                col.Unreadable(pr);
                                if (hashes != null) hashes[s.FirstPage + c / PageSize + i] = UnreadableHash;
                                continue;
                            }
                            if (hashes != null) hashes[s.FirstPage + c / PageSize + i] = Hash(_mem, i * PageSize, pl);
                            col.Feed(pr, _pri, _mem, i * PageSize, pl);
                        }
                    }
                    col.Close();
                }
            }
            return col.Out;
        }

        /// <summary>
        /// Modified places that touch the given pages (indices into the hash array), read in runs of consecutive pages with
        /// 64 bytes in front (a place that starts on the page before) and read on past the end while a place is still open.
        /// </summary>
        public List<CodeRegion> DiffPages(IList<int> pages)
        {
            var result = new List<CodeRegion>();
            if (pages == null || pages.Count == 0) return result;
            var sorted = new List<int>(pages);
            sorted.Sort();
            var seen = new HashSet<int>();
            var fs = _fs;
            {
                int i = 0;
                while (i < sorted.Count)
                {
                    Section s = SectionOfPage(sorted[i]);
                    if (s == null) { i++; continue; }
                    int first = sorted[i], last = first;
                    while (i + 1 < sorted.Count && sorted[i + 1] <= last + 1 && sorted[i + 1] < s.FirstPage + s.Pages) { i++; last = sorted[i]; }
                    i++;
                    int runStart = s.Rva + (first - s.FirstPage) * PageSize;
                    int runEnd = Math.Min(s.Rva + s.Size, s.Rva + (last - s.FirstPage + 1) * PageSize);
                    foreach (var r in DiffRange(fs, s, Math.Max(s.Rva, runStart - 64), runEnd))
                        if (r.End > runStart && r.Rva < runEnd && seen.Add(r.Rva)) result.Add(r);
                }
            }
            return result;
        }

        private List<CodeRegion> DiffRange(FileStream fs, Section s, int from, int to)
        {
            var col = new Collector();
            int secEnd = s.Rva + s.Size, limit = Math.Min(secEnd, to + 65536);
            int pos = from;
            while (pos < to || (col.Open && pos < limit))
            {
                // work on whole pages (at most one chunk) for reading, compare only [pos, end)
                int pageStart = pos & ~(PageSize - 1);
                int end = pos < to ? Math.Min(to, pageStart + Chunk) : Math.Min(limit, pos + 256);
                int pageEnd = Math.Min(secEnd, (end + PageSize - 1) & ~(PageSize - 1));
                int span = pageEnd - pageStart;
                bool[] readable = ReadablePages(pageStart, span);
                var okPages = new bool[(span + PageSize - 1) / PageSize];
                ReadChunk(pageStart, span, readable, 0, okPages);
                ReadPristine(fs, s, pageStart, _pri, span);
                Mask(pageStart, _pri, _mem, span);
                for (int p = 0; p < okPages.Length; p++)
                {
                    int pr = pageStart + p * PageSize;
                    int a = Math.Max(pos, pr), b = Math.Min(end, Math.Min(pageEnd, pr + PageSize));
                    if (a >= b) continue;
                    if (!okPages[p]) { if (a == pr || a == pos) col.Unreadable(pr); continue; }
                    col.Feed(a, _pri, _mem, a - pageStart, b - a);
                }
                pos = end;
            }
            col.Close();
            return col.Out;
        }

        private Section SectionOfPage(int page)
        {
            foreach (var s in _exec) if (page >= s.FirstPage && page < s.FirstPage + s.Pages) return s;
            return null;
        }

        /// <summary>Hash-array indices of the pages a place touches.</summary>
        public void PagesOf(CodeRegion r, ICollection<int> into)
        {
            foreach (var s in _exec)
            {
                if (r.Rva >= s.Rva + s.Size || r.End <= s.Rva) continue;
                int a = (Math.Max(r.Rva, s.Rva) - s.Rva) / PageSize, b = (Math.Min(r.End, s.Rva + s.Size) - 1 - s.Rva) / PageSize;
                for (int p = a; p <= b; p++) into.Add(s.FirstPage + p);
            }
        }

        /// <summary>Name of the executable section holding <paramref name="rva"/> ("?" outside).</summary>
        public string SectionName(int rva)
        {
            foreach (var s in _exec) if (rva >= s.Rva && rva < s.Rva + s.Size) return s.Name;
            return "?";
        }

        /// <summary>"name size, name size" of the executable sections, for the log.</summary>
        public string Layout()
        {
            var sb = new StringBuilder();
            foreach (var s in _exec)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(s.Name).Append(' ').Append((s.Size / 1048576.0).ToString("0.0")).Append(" MB");
            }
            return sb.ToString();
        }

        /// <summary>
        /// For the log: where a modified place sends the code, when it starts with a jump / call / push-ret / int3, and what
        /// owns that address ("in X.dll", or memory outside any DLL, which is what a manually mapped cheat looks like).
        /// </summary>
        public string DescribeJump(CodeRegion r)
        {
            if (r.Unreadable) return "page not readable (guard / no-access)";
            try
            {
                var b = new byte[16];
                if (!ReadMemory(r.Rva, b, b.Length)) return "";
                long here = Base.ToInt64() + r.Rva, target = 0;
                string kind = null;
                if (b[0] == 0xE9 || b[0] == 0xE8) { kind = b[0] == 0xE9 ? "jmp" : "call"; target = here + 5 + BitConverter.ToInt32(b, 1); }
                else if (b[0] == 0xFF && b[1] == 0x25)
                {
                    kind = "jmp [ ]";
                    long slot = Is64 ? here + 6 + BitConverter.ToInt32(b, 2) : (long)BitConverter.ToUInt32(b, 2);
                    var p = new byte[8];
                    if (ReadAbs(slot, p, Is64 ? 8 : 4)) target = Is64 ? BitConverter.ToInt64(p, 0) : BitConverter.ToUInt32(p, 0);
                }
                else if (!Is64 && b[0] == 0x68 && b[5] == 0xC3) { kind = "push+ret"; target = BitConverter.ToUInt32(b, 1); }
                else if (Is64 && b[0] == 0x48 && b[1] == 0xB8 && b[10] == 0xFF && b[11] == 0xE0) { kind = "mov rax+jmp"; target = BitConverter.ToInt64(b, 2); }
                else if (b[0] == 0xCC) return "int3 (breakpoint)";
                if (kind == null) return "";
                if (!Is64) target &= 0xFFFFFFFFL;
                return kind + " 0x" + target.ToString("X") + " (" + Owner(target) + ")";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>"in X.dll" (file name only) or the kind of memory an address lies in.</summary>
        public static string Owner(long address)
        {
            IntPtr h;
            if (address != 0 && GetModuleHandleExW(0x4 | 0x2, new IntPtr(address), out h) && h != IntPtr.Zero)   // FROM_ADDRESS | UNCHANGED_REFCOUNT
            {
                string p = ModulePath(h);
                return "in " + (p == null ? "a module" : Path.GetFileName(p));
            }
            Mbi m;
            if (VirtualQuery(new IntPtr(address), out m, new IntPtr(Marshal.SizeOf(typeof(Mbi)))) == IntPtr.Zero || m.State != 0x1000)
                return "not mapped";
            if (m.Type == 0x20000) return "memory outside any DLL (private)";
            if (m.Type == 0x40000) return "memory outside any DLL (mapped)";
            return "an image without a module entry (manually mapped?)";
        }

        // ------------------------------------------------------------------------------------------ little-endian helpers

        private static int U16(byte[] b, int o) { return b[o] | (b[o + 1] << 8); }
        private static int I32(byte[] b, int o) { return BitConverter.ToInt32(b, o); }
        private static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
        private static ulong U64(byte[] b, int o) { return BitConverter.ToUInt64(b, o); }
        private static void W16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static void W32(byte[] b, int o, uint v) { for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (8 * i)); }
        private static void W64(byte[] b, int o, ulong v) { for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (8 * i)); }

        /// <summary>Up to <paramref name="max"/> bytes as hex ("e9 12 34 …").</summary>
        public static string Hex(byte[] b, int max)
        {
            if (b == null) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < b.Length && i < max; i++) { if (i > 0) sb.Append(' '); sb.Append(b[i].ToString("x2")); }
            if (b.Length > max) sb.Append(" …");
            return sb.ToString();
        }
    }

    /// <summary>
    /// The accepted state of the code: a baseline taken once BepInEx and Harmony have finished hooking, then cheap checks
    /// (page hashes) against it. A modified place is "known" once accepted (a hook at a BepInEx / Harmony site, or one
    /// already reported); anything else found later is a suspect until <see cref="Confirm"/> looks again.
    /// </summary>
    internal sealed class CodeWatch
    {
        /// <summary>
        /// Bytes from a hook site a BepInEx native detour may write: Dobby writes a 5-byte "jmp rel32" on x86 and a 14-byte
        /// "jmp [rip]; dq" on x64 at the function's first byte.
        /// </summary>
        public const int HookWindow = 16;

        public readonly CodeImage Image;
        private ulong[] _accepted;
        private readonly Dictionary<int, byte[]> _known = new Dictionary<int, byte[]>();
        private readonly HashSet<int> _unreadableKnown = new HashSet<int>();
        private int[] _baseSites = new int[0];

        public CodeWatch(CodeImage image) { Image = image; }

        public bool HasBaseline { get { return _accepted != null; } }
        public int KnownCount { get { return _known.Count + _unreadableKnown.Count; } }

        /// <summary>The place lies inside the window of one of the sites (sorted RVAs).</summary>
        public static bool InWindow(CodeRegion r, int[] sortedSites) { return SiteOf(r, sortedSites) >= 0; }

        /// <summary>The site whose window holds the place, or -1.</summary>
        private static int SiteOf(CodeRegion r, int[] sortedSites)
        {
            if (sortedSites == null || sortedSites.Length == 0 || r.Unreadable) return -1;
            // the last site at or before the place's start
            int lo = 0, hi = sortedSites.Length - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (sortedSites[mid] <= r.Rva) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best >= 0 && r.End <= (long)sortedSites[best] + HookWindow ? sortedSites[best] : -1;
        }

        /// <summary>Sorted, distinct copy.</summary>
        public static int[] Sorted(IEnumerable<int> sites)
        {
            var set = new SortedSet<int>(sites);
            var a = new int[set.Count];
            set.CopyTo(a);
            return a;
        }

        /// <summary>
        /// Full comparison. Every place found becomes known; <paramref name="unexplained"/> gets those outside every site window
        /// (for the caller to look at again and report).
        /// </summary>
        public List<CodeRegion> Baseline(int[] sortedSites, List<CodeRegion> unexplained)
        {
            var hashes = new ulong[Image.PageCount];
            var all = Image.FullDiff(hashes);
            _known.Clear(); _unreadableKnown.Clear();
            foreach (var r in all)
            {
                Remember(r);
                if (!InWindow(r, sortedSites)) unexplained.Add(r);
            }
            _baseSites = sortedSites ?? new int[0];
            _accepted = hashes;
            return all;
        }

        private void Remember(CodeRegion r)
        {
            if (r.Unreadable) _unreadableKnown.Add(r.Rva);
            else _known[r.Rva] = r.Now;
        }

        private bool IsKnown(CodeRegion r)
        {
            if (r.Unreadable) return _unreadableKnown.Contains(r.Rva);
            byte[] k;
            return _known.TryGetValue(r.Rva, out k) && r.SameBytes(k);
        }

        /// <summary>
        /// Hash pass. Returns the places in changed pages that are not known. Known places that are gone (the code is original
        /// there again: a hook was removed) are forgotten and returned in <paramref name="reverted"/>. Pages whose changes are
        /// all known get their new hash accepted.
        /// </summary>
        public List<CodeRegion> Suspects(List<CodeRegion> reverted)
        {
            var result = new List<CodeRegion>();
            if (_accepted == null) return result;
            var now = new ulong[Image.PageCount];
            Image.HashAll(now);
            var changed = new List<int>();
            for (int i = 0; i < now.Length; i++) if (now[i] != _accepted[i]) changed.Add(i);
            if (changed.Count == 0) return result;

            var regions = Image.DiffPages(changed);
            var changedSet = new HashSet<int>(changed);
            var present = new HashSet<int>();
            foreach (var r in regions) present.Add(r.Rva);
            var pages = new List<int>();
            // known places inside the changed pages that are no longer there
            var gone = new List<int>();
            foreach (var kv in _known)
            {
                if (present.Contains(kv.Key)) continue;
                pages.Clear();
                Image.PagesOf(new CodeRegion(kv.Key, kv.Value, kv.Value), pages);
                foreach (int p in pages) if (changedSet.Contains(p)) { gone.Add(kv.Key); break; }
            }
            foreach (int rva in gone) { reverted.Add(new CodeRegion(rva, null, _known[rva])); _known.Remove(rva); }
            var goneUnreadable = new List<int>();
            foreach (int rva in _unreadableKnown)
            {
                if (present.Contains(rva)) continue;
                pages.Clear();
                Image.PagesOf(new CodeRegion(rva, null, null), pages);
                foreach (int p in pages) if (changedSet.Contains(p)) { goneUnreadable.Add(rva); break; }
            }
            foreach (int rva in goneUnreadable) _unreadableKnown.Remove(rva);

            var suspectPages = new HashSet<int>();
            foreach (var r in regions)
            {
                if (IsKnown(r)) continue;
                result.Add(r);
                Image.PagesOf(r, suspectPages);
            }
            foreach (int p in changed) if (!suspectPages.Contains(p)) _accepted[p] = now[p];
            return result;
        }

        /// <summary>
        /// Second look after a pause. Of the places in the suspects' pages, returns those still not known. A place inside the
        /// window of a site that was not a hook site at the baseline (BepInEx / Harmony hooked a new function since) is
        /// accepted instead and returned in <paramref name="acceptedLegit"/>. A changed place at a baseline site is NOT
        /// accepted: nothing re-hooks after the baseline, so that is someone overwriting a hook.
        /// </summary>
        public List<CodeRegion> Confirm(List<CodeRegion> suspects, int[] sortedSitesNow, List<CodeRegion> acceptedLegit)
        {
            var bad = new List<CodeRegion>();
            if (suspects == null || suspects.Count == 0) return bad;
            var pages = new HashSet<int>();
            foreach (var r in suspects) Image.PagesOf(r, pages);
            var newSites = new List<int>();
            if (sortedSitesNow != null)
                foreach (int s in sortedSitesNow) if (Array.BinarySearch(_baseSites, s) < 0) newSites.Add(s);
            int[] fresh = Sorted(newSites);
            var fired = new List<int>(_baseSites);
            foreach (var r in Image.DiffPages(new List<int>(pages)))
            {
                if (IsKnown(r)) continue;
                int site = SiteOf(r, fresh);
                if (site >= 0) { acceptedLegit.Add(r); fired.Add(site); continue; }
                bad.Add(r);
            }
            if (acceptedLegit.Count > 0)
            {
                // from now on these are hook sites like the baseline's: a later change there is not a new hook
                _baseSites = Sorted(fired);
                Accept(acceptedLegit);
            }
            return bad;
        }

        /// <summary>
        /// Second look at places found by <see cref="Baseline"/>: those whose start still shows a modified place now (the
        /// current bytes; a place that went back to the original code is dropped).
        /// </summary>
        public List<CodeRegion> StillModified(List<CodeRegion> places)
        {
            var still = new List<CodeRegion>();
            if (places == null || places.Count == 0) return still;
            var pages = new HashSet<int>();
            foreach (var r in places) Image.PagesOf(r, pages);
            var now = new Dictionary<int, CodeRegion>();
            foreach (var r in Image.DiffPages(new List<int>(pages))) now[r.Rva] = r;
            foreach (var r in places)
            {
                CodeRegion c;
                if (now.TryGetValue(r.Rva, out c)) still.Add(c);
            }
            return still;
        }

        /// <summary>The places become known (after they were reported or accepted) and their pages' current hashes are accepted.</summary>
        public void Accept(IList<CodeRegion> regions)
        {
            if (_accepted == null || regions == null || regions.Count == 0) return;
            var pages = new HashSet<int>();
            foreach (var r in regions) { Remember(r); Image.PagesOf(r, pages); }
            var now = new ulong[Image.PageCount];
            Image.HashAll(now);
            foreach (int p in pages) _accepted[p] = now[p];
        }
    }

    /// <summary>
    /// Names of the loaded kernel drivers, read in user mode. Windows 11 24H2 and later hide kernel addresses from programs
    /// without SeDebugPrivilege: EnumDeviceDrivers then returns only zeros and GetDeviceDriverBaseName names every entry
    /// "ntoskrnl.exe" (seen on this PC, build 26200). NtQuerySystemInformation(SystemModuleInformation), which
    /// EnumDeviceDrivers itself is built on, still returns the names, so it is used first; EnumDeviceDrivers +
    /// GetDeviceDriverBaseName only when that call fails and real addresses come back. Nothing is loaded or installed.
    /// </summary>
    internal static class KernelDrivers
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returned);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EnumDeviceDrivers([Out] IntPtr[] addresses, uint bytes, out uint needed);

        [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetDeviceDriverBaseNameW(IntPtr address, StringBuilder name, uint size);

        private const int SystemModuleInformation = 11;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

        /// <summary>(file name, kernel path) of every loaded driver; empty with <paramref name="error"/> when neither API works.</summary>
        public static List<KeyValuePair<string, string>> Loaded(out string how, out string error)
        {
            how = "NtQuerySystemInformation";
            string e1;
            var list = FromNtQuery(out e1);
            if (list != null) { error = null; return list; }
            how = "EnumDeviceDrivers";
            string e2;
            list = FromEnumDeviceDrivers(out e2);
            if (list != null) { error = null; return list; }
            error = e1 + "; " + e2;
            return new List<KeyValuePair<string, string>>();
        }

        private static List<KeyValuePair<string, string>> FromNtQuery(out string error)
        {
            error = null;
            int len = 256 * 1024;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    int ret;
                    int st = NtQuerySystemInformation(SystemModuleInformation, buf, len, out ret);
                    if (st == StatusInfoLengthMismatch) { len = Math.Max(len * 2, ret + 16384); continue; }
                    if (st != 0) { error = "NtQuerySystemInformation 0x" + st.ToString("X8"); return null; }
                    int count = Marshal.ReadInt32(buf), ptr = IntPtr.Size;
                    int entry = ptr * 3 + 16 + 256, first = ptr;   // RTL_PROCESS_MODULE_INFORMATION; the array is pointer aligned
                    if (count < 0 || first + (long)count * entry > len) { error = "NtQuerySystemInformation: bad count " + count; return null; }
                    var list = new List<KeyValuePair<string, string>>(count);
                    var name = new byte[256];
                    for (int i = 0; i < count; i++)
                    {
                        long e = buf.ToInt64() + first + (long)i * entry;
                        int ofn = (ushort)Marshal.ReadInt16(new IntPtr(e + ptr * 3 + 14));
                        Marshal.Copy(new IntPtr(e + ptr * 3 + 16), name, 0, 256);
                        int z = Array.IndexOf(name, (byte)0);
                        if (z < 0) z = 256;
                        var sb = new StringBuilder(z);
                        for (int k = 0; k < z; k++) sb.Append((char)name[k]);   // ANSI path; driver names are ASCII
                        string full = sb.ToString();
                        string file = ofn > 0 && ofn < full.Length ? full.Substring(ofn) : BaseName(full);
                        if (file.Length > 0) list.Add(new KeyValuePair<string, string>(file, full));
                    }
                    return list;
                }
                catch (Exception ex) { error = "NtQuerySystemInformation: " + ex.Message; return null; }
                finally { Marshal.FreeHGlobal(buf); }
            }
            error = "NtQuerySystemInformation: buffer kept growing";
            return null;
        }

        private static List<KeyValuePair<string, string>> FromEnumDeviceDrivers(out string error)
        {
            error = null;
            try
            {
                uint needed;
                EnumDeviceDrivers(null, 0, out needed);
                var a = new IntPtr[needed / (uint)IntPtr.Size + 64];
                if (!EnumDeviceDrivers(a, (uint)(a.Length * IntPtr.Size), out needed)) { error = "EnumDeviceDrivers failed"; return null; }
                int n = Math.Min(a.Length, (int)(needed / (uint)IntPtr.Size));
                var distinct = new HashSet<long>();
                for (int i = 0; i < n; i++) if (a[i] != IntPtr.Zero) distinct.Add(a[i].ToInt64());
                if (distinct.Count < n / 2) { error = "EnumDeviceDrivers: kernel addresses hidden"; return null; }
                var list = new List<KeyValuePair<string, string>>(n);
                var sb = new StringBuilder(260);
                for (int i = 0; i < n; i++)
                {
                    if (a[i] == IntPtr.Zero) continue;
                    sb.Length = 0;
                    if (GetDeviceDriverBaseNameW(a[i], sb, (uint)sb.Capacity) > 0) list.Add(new KeyValuePair<string, string>(sb.ToString(), ""));
                }
                return list;
            }
            catch (Exception ex) { error = "EnumDeviceDrivers: " + ex.Message; return null; }
        }

        private static string BaseName(string full)
        {
            int i = full.LastIndexOfAny(new[] { '\\', '/' });
            return i >= 0 ? full.Substring(i + 1) : full;
        }
    }
}
