using System.IO.Compression;
using System.Text;
static class Program
{
    static readonly byte[] WFLZ = { 0x57, 0x46, 0x4C, 0x5A };
    static readonly byte[] PNG_SIG = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static int Main()
    {
        string gamePath = getGamePath();
        string dataPath = Path.Combine(gamePath, "data");
        string extractRoot = Path.Combine(dataPath, "extracted");
        string spritesRoot = Path.Combine(dataPath, "extracted_sprites");
        string stbRoot = Path.Combine(dataPath, "extracted_tables");

        ExtractArchives(dataPath, extractRoot);
        ExtractSprites(extractRoot, spritesRoot);
        ExtractSTB(extractRoot, stbRoot);

        return 0;
    }
    static private string getGamePath()
    {
        string gamePath = "";

        // Try common install locations
        bool foundPath = false;
        var possiblePaths = GetPossibleGamePaths();
        foreach (string path in possiblePaths) {
            if (IsValidGamePath(path)) {
                Console.WriteLine($"Mina install path found at {path}\n");
                return path;
            }
        }
        if (!foundPath) {
            Console.WriteLine($"Mina install path not found automatically. Please enter path to 'Mina the Hollower' folder.\n");
            while (true) {
                gamePath = Console.ReadLine();
                if (IsValidGamePath(gamePath)) {
                    Console.WriteLine($"Mina install path set to {gamePath}\n");
                    return gamePath;
                }
                Console.WriteLine($"Not a valid Mina install path. Please enter path to 'Mina the Hollower' folder.\n");
            }
        }
        return gamePath;
    }
    static private List<string> GetPossibleGamePaths()
    {
        string GAME_NAME = "Mina the Hollower";
        var paths = new List<string>();

        // Windows Steam paths
        paths.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", GAME_NAME));
        paths.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Steam", "steamapps", "common", GAME_NAME));

        // Common alternate Steam library locations
        paths.Add(@"D:\SteamLibrary\steamapps\common\" + GAME_NAME);
        paths.Add(@"E:\SteamLibrary\steamapps\common\" + GAME_NAME);

        return paths;
    }
    static private bool IsValidGamePath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;

        var dataPath = Path.Combine(path, "data");
        var exePath = Path.Combine(path, "MinaTheHollower.exe");

        return File.Exists(exePath) && Directory.Exists(dataPath);
    }
    static private void ExtractArchives(string dataPath, string extractRoot)
    {
        Console.WriteLine($"Extracting files to {extractRoot}...");

        // Delete if already exists, create if not
        if (Directory.Exists(extractRoot)) {
            Directory.Delete(extractRoot, true);
        }
        else {
            Directory.CreateDirectory(extractRoot);
        }

        int archives = 0, files = 0;
        foreach (var file in Directory.GetFiles(dataPath, "*.yc")) {
            string filename = Path.GetFileName(file);
            string extractPath = Path.Combine(extractRoot, filename.Substring(0, filename.Length - 7));

            try {
                byte[] f = File.ReadAllBytes(file);
                int n = ExtractContainer(f, 0, extractPath);
                archives++; files += n;
                //Console.WriteLine($"Extracted {filename}: {n} members");
            }
            catch (Exception e) {
                Console.Error.WriteLine($"[{file}] {e.Message}");
            }
        }
        Console.WriteLine($"Done! Extracted {files} files from {archives} archives.\n");
    }

    // Parse the YCD container at `base`. If its node table is a directory,
    // write each member under its real name (recursing into members that are
    // themselves directories). Returns the number of leaf files written.
    static int ExtractContainer(byte[] f, int @base, string extractPath)
    {
        if (!IsYcd(f, @base)) return 0;
        int dir = @base + (int)U64(f, @base + 8); // base + rootOffset
        if (!IsDirectory(f, dir)) return 0; // leaf container, nothing to split

        int count = (int)(U32(f, dir + 4) / 4);
        int entries = dir + 0x10;
        Directory.CreateDirectory(extractPath);
        int written = 0;

        for (int i = 0; i < count; i++) {
            int e = entries + i * 0x20;
            if (e + 0x20 > f.Length) break;

            int nameLen = (int)U32(f, e + 0x04);
            int nameAddr = (e + 0x08) + I32(f, e + 0x0C); // self-relative to +0x08
            int size = (int)U32(f, e + 0x10);
            int dataAddr = (e + 0x18) + I32(f, e + 0x1C); // self-relative to +0x18

            if (nameAddr < 0 || nameAddr + nameLen > f.Length) continue;
            if (dataAddr < 0 || (long)dataAddr + size > f.Length || size <= 0) continue;

            string name = Encoding.ASCII.GetString(f, nameAddr, nameLen);
            string outPath = SanitizePath(name);

            // A member is itself a YCD; if IT carries a directory, recurse into a folder.
            if (IsYcd(f, dataAddr) && IsDirectory(f, dataAddr + (int)U64(f, dataAddr + 8))) {
                written += ExtractContainer(f, dataAddr, Path.Combine(extractPath, outPath));
                continue;
            }

            // Leaf asset: write the raw member bytes under its real name.
            string dst = Path.Combine(extractPath, outPath);
            string dstDir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
            File.WriteAllBytes(dst, Slice(f, dataAddr, size));
            written++;
        }
        return written;
    }
    static string SanitizePath(string name)
    {
        var parts = name.Replace('\\', '/').Split('/');
        for (int k = 0; k < parts.Length; k++) {
            string s = parts[k];
            if (string.IsNullOrEmpty(s) || s == "." || s == "..") { parts[k] = "_"; continue; }
            var sb = new StringBuilder();
            foreach (char ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-' ? ch : '_');
            parts[k] = sb.ToString();
        }
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }
    static byte[] Slice(byte[] f, int o, int n) { var b = new byte[n]; Buffer.BlockCopy(f, o, b, 0, n); return b; }
    static uint U32(byte[] f, int o) => (uint)(f[o] | (f[o + 1] << 8) | (f[o + 2] << 16) | (f[o + 3] << 24));
    static int I32(byte[] f, int o) => (int)U32(f, o);
    static ulong U64(byte[] f, int o) { ulong v = 0; for (int k = 0; k < 8; k++) v |= (ulong)f[o + k] << (8 * k); return v; }
    // ============================================================
    // == SPRITES == //
    //  Each .anb is a YCD leaf whose tail holds one or more WFLZ
    //  blobs of 8-bit palette indices, one per sprite "cell". Each
    //  cell is a 0x20-byte record; the cell array ends exactly where
    //  the wfLZ section (U64 @0x30) begins:
    //
    //      cellArrayStart = wflzSectionStart - numChunks*0x20
    //
    //  Cell layout (0x20 bytes):
    //      +0x00 u32  width
    //      +0x04 u32  height
    //      +0x10 u32  chunkSize  (== wfLZ compressedSize + 16)
    //      +0x18 u32  marker     (0x20, or 0xFFFFFFFF on the last)
    //      +0x1C i32  ptr -> wfLZ chunk, base = (cell+0x18)
    //
    //  WFLZ header (16 bytes): "WFLZ", u32 compressedSize,
    //  u32 decompressedSize, firstBlock{u16 dist,u8 len,u8 nLits}.
    //  decompressedSize == width*height (1 byte/pixel = palette index).
    //
    //  Colors come from the referenced palette file.
    //  Palette files are also YCD leafs:
    //  root = U64@0x08; capacity = U32@root; an RGBA (R,G,B,A)
    //  Color table begins at root+0x28; index 0 is transparent.
    //  Sprites index that table directly, so PNGs are written as
    //  8-bit INDEXED images carrying the real PLTE + tRNS. If the
    //  palette can't be found fall back to a debug palette.
    // ============================================================
    static private void ExtractSprites(string extractRoot, string spritesRoot)
    {
        Console.WriteLine($"Extracting sprites to {spritesRoot}...");

        // Delete if already exists, create if not
        if (Directory.Exists(spritesRoot)) {
            Directory.Delete(spritesRoot, true);
        }
        else {
            Directory.CreateDirectory(spritesRoot);
        }

        int banks = 0, sprites = 0;
        foreach (var anb_file in Directory.EnumerateFiles(extractRoot, "*.anb.yc", SearchOption.AllDirectories)) {
            bool noPalette = false;
            string relativePath = Path.GetRelativePath(extractRoot, anb_file).Replace(".anb.yc", "");
            string extractPath = Path.Combine(spritesRoot, relativePath);

            byte[] f;
            try { f = File.ReadAllBytes(anb_file); } catch { continue; }

            if (!IsYcd(f, 0)) continue;
            if (IndexOf(f, WFLZ, 0x100) < 0) continue; // must have at least one sprite blob

            try {
                int n = ExtractBank(f, extractPath, extractRoot, anb_file, noPalette);
                banks++; sprites += n;
                //Console.WriteLine($"Extracted {filename}: {n} sprites");
            }
            catch (Exception e) {
                Console.Error.WriteLine($"[{anb_file}] {e.Message}");
            }
        }
        Console.WriteLine($"Done! Extracted {sprites} sprites from {banks} banks.\n");
    }
    static int ExtractBank(byte[] f, string extractPath, string palRoot, string anbDir, bool noPalette)
    {
        // WFLZ section start: prefer the header pointer, fall back to a scan.
        long ss = (long)U64(f, 0x30);
        int sect = (ss > 0 && ss + 16 <= f.Length && Match(f, (int)ss, WFLZ)) ? (int)ss : IndexOf(f, WFLZ, 0x100);
        if (sect < 0) return 0;

        // enumerate every WFLZ chunk (packed tight: stride = 16 + compressedSize)
        var chunks = new List<(int off, int comp, int dec)>();
        for (int p = sect; p + 16 <= f.Length && Match(f, p, WFLZ);) {
            int comp = (int)U32(f, p + 4), dec = (int)U32(f, p + 8);
            chunks.Add((p, comp, dec));
            if (comp <= 0) break;
            p += 16 + comp;
        }
        int nc = chunks.Count;
        if (nc == 0) return 0;

        Directory.CreateDirectory(extractPath);

        // locate the referenced palette and load its real colors
        string palRef = null;
        foreach (var s in FindStrings(f, 0x100, sect)) {
            if (s.EndsWith(".pal.yc", StringComparison.OrdinalIgnoreCase)) { palRef = s; break; }
            if (palRef == null && s.Contains(".pal")) palRef = s; // .png ref as a weaker hint
        }

        byte[] rgb, alpha;
        string usedPath = null;
        (byte[] rgb, byte[] alpha)? loaded = null;
        if (!noPalette && palRef != null) loaded = LoadPaletteFor(palRoot, anbDir, palRef, out usedPath);
        if (loaded.HasValue) {
            rgb = loaded.Value.rgb;
            alpha = loaded.Value.alpha;
        }
        else {
            // Console.WriteLine($"Palette not found for '{palRef ?? "none"}', using debug palette.");
            var dbg = BuildPalette(); rgb = dbg.rgb; alpha = dbg.alpha;
        }
        int cellBase = sect - nc * 0x20;
        string[] cellNames = BuildCellNames(f, sect, nc);
        int written = 0;

        for (int k = 0; k < nc; k++) {
            var (off, comp, dec) = chunks[k];
            int w = 0, h = 0; string how = "cell";

            // primary: positional cell record
            int cell = cellBase + k * 0x20;
            if (cell >= 0x100 && cell + 0x20 <= sect) {
                int cw = (int)U32(f, cell), ch = (int)U32(f, cell + 4);
                if ((long)cw * ch == dec) { w = cw; h = ch; }
            }
            // fallback: find the cell by its back-pointer to this chunk
            if (w == 0 || h == 0) (w, h, how) = ResolveByPointer(f, off, dec, sect);
            // last resort: nearest-square shaping so nothing is lost
            if ((long)w * h != dec) { (w, h) = NearSquare(dec); how = "inferred"; }

            byte[] idx;
            try { idx = WflzDecompress(f, off); } catch { idx = Array.Empty<byte>(); }

            string stem = cellNames != null && cellNames[k] != null ? cellNames[k] : $"{k:D4}";
            string file = Path.Combine(extractPath, stem + ".png");
            WriteIndexedPng(file, w, h, idx, rgb, alpha);
            written++;
        }
        return written;
    }

    // Search 4-aligned positions for the chunk-pointer half of a cell whose
    // self-relative pointer resolves to `chunkOff`; W/H sit 0x10 bytes before it.
    static (int w, int h, string how) ResolveByPointer(byte[] f, int chunkOff, int dec, int sect)
    {
        for (int p = 0x100; p + 0x10 <= sect; p += 4) {
            uint marker = U32(f, p + 8);
            if (marker != 0x20 && marker != 0xFFFFFFFF) continue;
            long target = (long)(p + 8) + I32(f, p + 0x0C);
            if (target != chunkOff) continue;
            int cell = p - 0x10;
            if (cell < 0x100) continue;
            int w = (int)U32(f, cell), h = (int)U32(f, cell + 4);
            if ((long)w * h == dec) return (w, h, "ptr");
        }
        return (0, 0, "ptr-miss");
    }

    static (int w, int h) NearSquare(int n)
    {
        if (n <= 1) return (Math.Max(n, 1), 1);
        for (int d = (int)Math.Sqrt(n); d >= 1; d--)
            if (n % d == 0) return (n / d, d);   // w >= h
        return (n, 1);
    }

    // ============================================================
    // == SPRITE NAMES == //
    //  A bank holds a small named-tag table just past the header's
    //  palette-ref schema. Each tag is a FieldDesc-style record
    //  (stride 0x38):
    //      +0x00 u32 zeroes (0)
    //      +0x04 u32 nameLen
    //      +0x08 u32 kind   (0x10)
    //      +0x0C i32 off     name @ (rec+0x08)+off  (a simple identifier)
    //      +0x10 u32 span    frameCount = span / 0x10
    //  Tags appear in frame order and partition the sprite "pairs"
    //  contiguously (e.g. head=8, up/right/...=1 each, headOpen=8).
    //  Sprites are stored as (sprite, effect) pairs, interleaved, so
    //  cell 2p is frame p's drawn sprite and cell 2p+1 its effect.
    //  Multi-frame tags get a numeric suffix (head_00, ...) since the
    //  per-frame direction isn't stored; odd cells get "_fx". Returns
    //  null (-> numeric cell names) if the table can't be matched.
    // ============================================================
    static string[] BuildCellNames(byte[] f, int sect, int nc)
    {
        int start = (int)U64(f, 8); // offset1: schema/tag region
        if (start < 0 || start >= sect) return null;

        var tags = ParseTags(f, start, sect);          // name -> keyframe positions
        if (tags == null || tags.Count == 0) return null;
        var fdef = ParseFrameDef(f, start, sect, nc);   // position -> cell index(es)
        if (fdef == null) return null;

        // tag -> keyframe positions -> frame-def -> cell index(es).
        // First tag (TOC order) to claim a cell wins; cellB of a pair is the
        // effect ("_fx"); multi-frame tags number their frames (name_NN).
        var cells = new string[nc];
        foreach (var (name, count, kfPtr) in tags) {
            for (int i = 0; i < count; i++) {
                int kp = kfPtr + i * 0x18;
                if (kp < 0 || kp + 4 > f.Length) break;
                int pos = (int)U32(f, kp);
                if (pos < 0 || pos >= fdef.Count) continue;
                var (a, b) = fdef[pos];
                string stem = count > 1 ? $"{name}_{i:D2}" : name;
                if (a >= 0 && a < nc && cells[a] == null) cells[a] = stem;
                if (b >= 0 && b < nc && cells[b] == null) cells[b] = stem + "_subsprite";
            }
        }

        DeduplicateNames(cells); // unnamed cells stay null -> numeric in caller
        return cells;
    }
    // TOC: stride-0x38 tag entries (see IsTagEntry). Beyond the name/count each
    // entry points at its keyframe list: keyframes @ (rec+0x18) + i32(rec+0x1C),
    // 0x18 bytes each, whose first u32 is a frame-def position.
    static List<(string name, int count, int kfPtr)> ParseTags(byte[] f, int start, int sect)
    {
        int first = -1;
        for (int p = start; p + 0x20 <= sect; p += 4)
            if (IsTagEntry(f, p, sect)) { first = p; break; }
        if (first < 0) return null;

        var tags = new List<(string, int, int)>();
        for (int p = first; p + 0x20 <= sect && IsTagEntry(f, p, sect); p += 0x38) {
            int len = (int)U32(f, p + 4);
            int nameOff = I32(f, p + 0x0C);
            int count = (int)U32(f, p + 0x10) / 0x10;
            if (count < 1 || count > 4096) count = 1;
            int kfPtr = (p + 0x18) + I32(f, p + 0x1C);
            tags.Add((Encoding.ASCII.GetString(f, (p + 8) + nameOff, len), count, kfPtr));
        }
        return tags;
    }
    // Frame-def table: maps each animation frame position -> its cell index(es).
    //   record (0x38): u32 cellA, u32 cellB (0xFFFFFFFF = none), then per-record
    //   layout data (offsets/dimensions/sub-field pointers) whose shape varies.
    // cellB present => a (sprite, sub-sprite) pair. Some banks carry a 0x38 marker
    // at +0x20, but many don't (the layout tail differs per record), so instead the
    // table is identified by an invariant that holds everywhere: cellA equals the
    // running cell count (starts at 0; +1 per single record, +2 per pair). It is
    // walked until that count reaches nc; any deviation rejects the candidate so a
    // false start yields numeric names rather than wrong ones.
    static List<(int a, int b)> ParseFrameDef(byte[] f, int start, int sect, int nc)
    {
        for (int p0 = start; p0 + 0x20 <= sect; p0 += 4) {
            if (U32(f, p0) != 0) continue;                  // first record's cellA == 0
            uint kind = U32(f, p0 + 0x18);
            if (kind != 0x10 && kind != 0x20) continue;
            uint b0 = U32(f, p0 + 4);
            if (b0 != 0xFFFFFFFF && b0 >= (uint)nc) continue;

            var list = WalkFrameDef(f, p0, nc);
            if (list != null) return list;
        }
        return null;
    }
    static List<(int a, int b)> WalkFrameDef(byte[] f, int p0, int nc)
    {
        var list = new List<(int, int)>();
        int p = p0, acc = 0;
        while (acc < nc) {
            if (p + 8 > f.Length || (int)U32(f, p) != acc) return null;
            uint bu = U32(f, p + 4);
            if (bu == 0xFFFFFFFF) { list.Add((acc, -1)); acc += 1; }
            else if (bu < (uint)nc) { list.Add((acc, (int)bu)); acc += 2; }
            else return null;
            p += 0x38;
        }
        return acc == nc ? list : null;
    }

    // zeroes==0, kind==0x10, sane length, off resolves to a printable
    // identifier (alpha start; letters/digits/_), and a sane frame span.
    static bool IsTagEntry(byte[] f, int p, int end)
    {
        if (p < 0 || p + 0x14 > f.Length) return false;
        if (U32(f, p) != 0 || U32(f, p + 8) != 0x10) return false;
        int len = (int)U32(f, p + 4);
        if (len < 1 || len > 64) return false;
        int a = (p + 8) + I32(f, p + 0x0C);
        if (a < 0 || a >= end || (long)a + len > f.Length) return false;
        for (int i = 0; i < len; i++) {
            byte c = f[a + i];
            bool alpha = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            bool ok = alpha || (c >= '0' && c <= '9') || c == '_';
            if (!ok || (i == 0 && !alpha)) return false;
        }
        int count = (int)U32(f, p + 0x10) / 0x10;
        return count >= 1 && count <= 4096;
    }

    static void DeduplicateNames(string[] names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Length; i++) {
            if (names[i] == null) continue; // leave unnamed cells for numeric fallback
            string baseN = names[i];
            string n = baseN;
            int dup = 1;
            while (!seen.Add(n)) n = $"{baseN}_{dup++}";
            names[i] = n;
        }
    }
    // == PALETTES == //
    static readonly Dictionary<string, (byte[] rgb, byte[] alpha)?> _palCache =
        new Dictionary<string, (byte[] rgb, byte[] alpha)?>(StringComparer.OrdinalIgnoreCase);

    static (byte[] rgb, byte[] alpha)? LoadPaletteFor(string palRoot, string anbDir, string palRef, out string usedPath)
    {
        usedPath = null;
        foreach (var cand in PaletteCandidates(palRoot, anbDir, palRef)) {
            if (!_palCache.TryGetValue(cand, out var pal)) {
                pal = null;
                try { if (File.Exists(cand)) pal = LoadPalYc(File.ReadAllBytes(cand)); } catch { }
                _palCache[cand] = pal;
            }
            if (pal != null) { usedPath = cand; return pal; }
        }
        return null;
    }

    static IEnumerable<string> PaletteCandidates(string palRoot, string anbDir, string palRef)
    {
        string rel = palRef.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        yield return Path.Combine(palRoot, rel);
        if (anbDir != null) {
            yield return Path.Combine(anbDir, rel);
            var d = new DirectoryInfo(anbDir);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                yield return Path.Combine(d.FullName, rel);
            yield return Path.Combine(anbDir, Path.GetFileName(rel));
        }

        // Fallback: check each folder for this file
        if (Directory.Exists(palRoot)) {
            foreach (var subdir in Directory.GetDirectories(palRoot)) {
                yield return Path.Combine(subdir, rel);
            }
        }
    }

    // .pal.yc: YCD leaf. root = U64@0x08; capacity = U32@root;
    // RGBA (R,G,B,A) color table at root+0x28, one quad per slot.
    static (byte[] rgb, byte[] alpha)? LoadPalYc(byte[] f)
    {
        if (!IsYcd(f, 0)) return null;
        int root = (int)U64(f, 8);
        if (root < 0 || root + 0x28 > f.Length) return null;

        int capacity = (int)U32(f, root);
        if (capacity <= 0 || capacity > 256) capacity = 256;
        int colorStart = root + 0x28;
        if (colorStart + capacity * 4 > f.Length) capacity = Math.Max(0, (f.Length - colorStart) / 4);

        var rgb = new byte[256 * 3];
        var alpha = new byte[256];
        for (int i = 0; i < 256; i++) {
            if (i < capacity) {
                int p = colorStart + i * 4;
                rgb[i * 3] = f[p]; rgb[i * 3 + 1] = f[p + 1]; rgb[i * 3 + 2] = f[p + 2];
                alpha[i] = f[p + 3];
            }
            else alpha[i] = 0; // unused slots -> transparent
        }
        return (rgb, alpha);
    }
    static byte[] WflzDecompress(byte[] f, int hdr)
    {
        int decompSize = (int)U32(f, hdr + 8);
        var outBuf = new byte[decompSize < 0 ? 0 : decompSize];
        int o = 0;
        int src = hdr + 16; // first literal byte (after 16-byte header)
        int numLiterals = f[hdr + 15]; // firstBlock.numLiterals
        int dist = 0, len = 0;
        int end = f.Length;

        while (true) {
            if (numLiterals > 0) {
                do {
                    if (o >= outBuf.Length || src >= end) return outBuf;
                    outBuf[o++] = f[src++];
                } while (--numLiterals > 0);
            }
            else if (dist == 0 && len == 0) break; // terminator block

            if (src + 4 > end) break;
            dist = f[src] | (f[src + 1] << 8);
            len = f[src + 2];
            numLiterals = f[src + 3];
            src += 4;

            if (len != 0) {
                int matchLen = len + 4; // + (WFLZ_MIN_MATCH_LEN - 1)
                int cpy = o - dist;
                if (cpy < 0) break;
                for (int i = 0; i < matchLen && o < outBuf.Length; i++)
                    outBuf[o++] = outBuf[cpy + i]; // byte-wise: overlap-safe
            }
        }
        return outBuf;
    }
    static void WriteIndexedPng(string path, int w, int h, byte[] idx, byte[] rgb, byte[] alpha)
    {
        if (w <= 0) w = 1;
        if (h <= 0) h = 1;
        var raw = new byte[(w + 1) * h]; // filter byte 0 per scanline
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) {
                int i = y * w + x;
                raw[y * (w + 1) + 1 + x] = (byte)(i < idx.Length ? idx[i] : 0);
            }

        using var fs = File.Create(path);
        fs.Write(PNG_SIG, 0, 8);

        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)w);
        WriteBE(ihdr, 4, (uint)h);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 3; // color type 3 = indexed
        Chunk(fs, "IHDR", ihdr);
        Chunk(fs, "PLTE", rgb);
        Chunk(fs, "tRNS", alpha);
        Chunk(fs, "IDAT", Zlib(raw));
        Chunk(fs, "IEND", Array.Empty<byte>());
    }
    static (byte[] rgb, byte[] alpha) BuildPalette()
    {
        var rgb = new byte[256 * 3];
        var alpha = new byte[256];
        for (int i = 0; i < 256; i++) {
            if (i == 0) { alpha[0] = 0; continue; } // index 0 -> transparent
            HsvToRgb((i * 0.6180339887) % 1.0, 0.65, 1.0, out byte r, out byte g, out byte b);
            rgb[i * 3] = r; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = b;
            alpha[i] = 255;
        }
        return (rgb, alpha);
    }
    static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        double hh = h * 6.0; int i = (int)hh; double ff = hh - i;
        double p = v * (1 - s), q = v * (1 - s * ff), t = v * (1 - s * (1 - ff));
        double rr, gg, bb;
        switch (i % 6) {
            case 0: rr = v; gg = t; bb = p; break;
            case 1: rr = q; gg = v; bb = p; break;
            case 2: rr = p; gg = v; bb = t; break;
            case 3: rr = p; gg = q; bb = v; break;
            case 4: rr = t; gg = p; bb = v; break;
            default: rr = v; gg = p; bb = q; break;
        }
        r = (byte)(rr * 255); g = (byte)(gg * 255); b = (byte)(bb * 255);
    }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var t = Encoding.ASCII.GetBytes(type);
        var len = new byte[4]; WriteBE(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);

        EnsureCrcTable();
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < t.Length; i++)          // 1. type bytes first
            crc = _crcT[(crc ^ t[i]) & 0xFF] ^ (crc >> 8);
        for (int i = 0; i < data.Length; i++)        // 2. data bytes second
            crc = _crcT[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        crc ^= 0xFFFFFFFF;                           // 3. finalize

        var c = new byte[4]; WriteBE(c, 0, crc);
        s.Write(c, 0, 4);
    }
    static void EnsureCrcTable()
    {
        if (_crcT != null) return;
        _crcT = new uint[256];
        for (uint n = 0; n < 256; n++) {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            _crcT[n] = c;
        }
    }
    static byte[] Zlib(byte[] raw)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C); // zlib header
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
            ds.Write(raw, 0, raw.Length);
        uint a = Adler32(raw);
        ms.WriteByte((byte)(a >> 24)); ms.WriteByte((byte)(a >> 16));
        ms.WriteByte((byte)(a >> 8)); ms.WriteByte((byte)a);
        return ms.ToArray();
    }

    static uint[] _crcT;
    static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }
    static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
    static bool IsYcd(byte[] f, int o) =>
        o >= 0 && o + 8 <= f.Length &&
        f[o] == 0x59 && f[o + 1] == 0x43 && f[o + 2] == 0x44 && f[o + 3] == 0x00 && U32(f, o + 4) == 8;

    // Directory header looks like: [0x10*count][0x04*count][0x10][0x08]
    static bool IsDirectory(byte[] f, int o)
    {
        if (o < 0 || o + 16 > f.Length) return false;
        uint a = U32(f, o), b = U32(f, o + 4), c = U32(f, o + 8), d = U32(f, o + 12);
        return b != 0 && a == 4 * b && c == 0x10 && d == 0x08;
    }

    static bool Match(byte[] f, int o, byte[] sig)
    {
        if (o < 0 || o + sig.Length > f.Length) return false;
        for (int i = 0; i < sig.Length; i++) if (f[o + i] != sig[i]) return false;
        return true;
    }

    static int IndexOf(byte[] f, byte[] sig, int start)
    {
        for (int i = Math.Max(start, 0); i + sig.Length <= f.Length; i++)
            if (Match(f, i, sig)) return i;
        return -1;
    }

    static IEnumerable<string> FindStrings(byte[] f, int start, int end)
    {
        var sb = new StringBuilder();
        for (int i = Math.Max(start, 0); i < Math.Min(end, f.Length); i++) {
            byte c = f[i];
            if (c >= 0x20 && c < 0x7F) sb.Append((char)c);
            else { if (sb.Length >= 4) yield return sb.ToString(); sb.Clear(); }
        }
        if (sb.Length >= 4) yield return sb.ToString();
    }
    static private void ExtractSTB(string extractRoot, string stbRoot)
    {
        Console.WriteLine($"Extracting stb tables to {stbRoot}...");

        // Delete if already exists, create if not
        if (Directory.Exists(stbRoot)) {
            Directory.Delete(stbRoot, true);
        }
        else {
            Directory.CreateDirectory(stbRoot);
        }

        int tables = 0;
        foreach (var stb_file in Directory.EnumerateFiles(extractRoot, "*.stb.yc", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(extractRoot, stb_file).Replace(".stb.yc", ".csv");
            string outPath = Path.Combine(stbRoot, relativePath);

            byte[] f;
            try { f = File.ReadAllBytes(stb_file); } catch { continue; }
            if (!IsYcd(f, 0)) continue;

            try {
                int n = ExtractTable(f, outPath);
                if (n >= 0) {
                    tables++;
                }
            }
            catch (Exception e) {
                Console.Error.WriteLine($"[{stb_file}] {e.Message}");
            }
        }
        Console.WriteLine($"Done! Extracted {tables} stb tables.\n");
    }

    // ============================================================
    // // == STB == //
    //  An .stb.yc is a YCD leaf holding one data table (rows x cols
    //  of strings). Every section finds the next by a self-relative
    //  pointer, so column/row counts are derived, never assumed.
    //
    //    Header (0xC0 bytes): offset1 (U64 @0x08, == 0xC0) points past
    //      it to a directory block we don't need; scanning starts there.
    //
    //    Column metadata: first ColMeta-shaped record at/after offset1.
    //      ColMeta (0x10 bytes):
    //        +0x00 u32 zeroes  (0)
    //        +0x04 u32 len      name length, no NUL
    //        +0x08 u32 kind     0x10
    //        +0x0C i32 off      name @ (rec+0x08)+off
    //      colCount = (8 + meta[0].off) / 0x10   (the first column's off
    //      spans the whole metadata block onto the first name string).
    //
    //    Row index: array of 0x20-byte RowDescriptors, located by the
    //      (colCount*0x10, colCount*0x04, 0x10) signature.
    //        A @ +0x00 { u32 metaSize, u32 colCount*4, u32 0x10, i32 relRow }
    //        B @ +0x10 { u32 metaSize, u32 colCount*4, i32 ..  , i32 relMeta }
    //      A row's field block @ (B+0x08)+relMeta.
    //      rowCount = (8 + A.relRow) / 0x20  (row data sits right after
    //      the index, so the first descriptor's reach == index size).
    //
    //    Field block: colCount FieldDescs in column order.
    //      FieldDesc (0x10 bytes):
    //        +0x00 u32 zero
    //        +0x04 u32 length
    //        +0x08 u32 kind
    //        +0x0C i32 rel     value @ (rec+0x08)+rel; rel==-1 => empty.
    //      Pointers may be shared and may point backward (cells reuse
    //      strings across rows). Strings are UTF-8, byte length == len.
    // ============================================================
    static int ExtractTable(byte[] f, string outPath)
    {
        int colHdr = FindColHeaders(f, (int)U64(f, 8));
        if (colHdr < 0) return -1;

        int colCount = (8 + I32(f, colHdr + 0x0C)) / 0x10;
        if (colCount <= 0 || colCount > 4096) return -1;

        var headers = new string[colCount];
        for (int c = 0; c < colCount; c++) {
            int rec = colHdr + c * 0x10;
            headers[c] = ReadField(f, rec, I32(f, rec + 0x0C), (int)U32(f, rec + 0x04));
        }

        int rowIdx = FindRowIndex(f, colHdr + colCount * 0x10, (uint)(colCount * 0x10), (uint)(colCount * 0x04));
        if (rowIdx < 0) return -1;

        int rowCount = (8 + I32(f, rowIdx + 0x0C)) / 0x20;
        if (rowCount < 0 || (long)rowIdx + (long)rowCount * 0x20 > f.Length) return -1;

        string dstDir = Path.GetDirectoryName(outPath);
        if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);

        using var sw = new StreamWriter(outPath, false, new UTF8Encoding(true));
        sw.WriteLine(string.Join("\t", Array.ConvertAll(headers, CsvEscape)));

        int written = 0;
        for (int r = 0; r < rowCount; r++) {
            int b = rowIdx + r * 0x20 + 0x10;          // IndexRecordB
            int meta = (b + 0x08) + I32(f, b + 0x0C);  // field block
            if (meta < 0 || (long)meta + colCount * 0x10 > f.Length) continue;

            var cells = new string[colCount];
            for (int c = 0; c < colCount; c++) {
                int fd = meta + c * 0x10;
                cells[c] = ReadField(f, fd, I32(f, fd + 0x0C), (int)U32(f, fd + 0x04));
            }
            sw.WriteLine(string.Join("\t", Array.ConvertAll(cells, CsvEscape)));
            written++;
        }
        return written;
    }

    // Self-relative string: value @ (rec+0x08)+rel. rel==-1 means empty.
    // Backward/shared pointers are normal, so only the file bounds are checked.
    static string ReadField(byte[] f, int rec, int rel, int len)
    {
        if (rel == -1 || len <= 0) return "";
        int addr = (rec + 0x08) + rel;
        if (addr < 0 || (long)addr + len > f.Length) return "";
        return Encoding.UTF8.GetString(f, addr, len);
    }

    // First ColMeta after the header: zeroes==0, kind==0x10, a forward off
    // landing on printable ASCII. Skips the variable, unparsed directory.
    static int FindColHeaders(byte[] f, int start)
    {
        for (int p = Math.Max(start, 0); p + 0x10 <= f.Length; p += 4) {
            if (U32(f, p) != 0 || U32(f, p + 8) != 0x10) continue;
            int len = (int)U32(f, p + 4);
            int off = I32(f, p + 0x0C);
            if (off <= 0 || len < 1 || len > 0x100) continue;
            int t = (p + 8) + off;
            if (t < 0 || t >= f.Length) continue;
            if (f[t] >= 0x20 && f[t] < 0x7F) return p;
        }
        return -1;
    }

    // Row index start: the (colCount*0x10, colCount*0x04, 0x10) triple,
    // unique versus the ASCII name blob it scans past.
    static int FindRowIndex(byte[] f, int start, uint metaSize, uint second)
    {
        for (int p = Math.Max(start, 0); p + 12 <= f.Length; p += 4)
            if (U32(f, p) == metaSize && U32(f, p + 4) == second && U32(f, p + 8) == 0x10)
                return p;
        return -1;
    }

    static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}