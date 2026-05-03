using System.Text;

namespace GXW3Boost.Core;

/// <summary>
/// PEファイル（.exe/.dll）のインポートテーブルを静的解析し、
/// 依存DLL名の一覧を取得します。
/// GXW3.exeは32bitなのでPE32フォーマットを前提としますが、PE32+にも対応します。
/// </summary>
public static class PeImportParser
{
    public static IReadOnlyList<string> GetImportedDlls(string exePath)
    {
        var result = new List<string>();

        try
        {
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

            // --- DOSヘッダー検証 ---
            if (br.ReadUInt16() != 0x5A4D) return result; // "MZ"

            // PEヘッダーオフセット (0x3Cに格納)
            fs.Position = 0x3C;
            long peOffset = br.ReadUInt32();

            // --- PEシグネチャ検証 ---
            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) return result; // "PE\0\0"

            // --- COFFヘッダー (20 bytes) ---
            br.ReadUInt16();                          // Machine
            ushort numberOfSections = br.ReadUInt16();
            br.ReadUInt32();                          // TimeDateStamp
            br.ReadUInt32();                          // PointerToSymbolTable
            br.ReadUInt32();                          // NumberOfSymbols
            ushort sizeOfOptionalHeader = br.ReadUInt16();
            br.ReadUInt16();                          // Characteristics

            long optionalHeaderStart = fs.Position;

            // --- オプショナルヘッダー ---
            // PE32: Magic = 0x010B, PE32+: Magic = 0x020B
            ushort magic = br.ReadUInt16();
            bool isPE32Plus = magic == 0x020B;

            // インポートディレクトリはDataDirectory[1]
            // PE32 : オプショナルヘッダー先頭から 104バイト目
            // PE32+: オプショナルヘッダー先頭から 120バイト目
            int importDirRelOffset = isPE32Plus ? 120 : 104;
            fs.Position = optionalHeaderStart + importDirRelOffset;

            uint importDirRva = br.ReadUInt32();
            uint importDirSize = br.ReadUInt32();

            if (importDirRva == 0 || importDirSize == 0) return result;

            // --- セクションヘッダー (各40 bytes) ---
            fs.Position = optionalHeaderStart + sizeOfOptionalHeader;

            var sections = new (uint VirtualAddress, uint PointerToRawData, uint SizeOfRawData)[numberOfSections];
            for (int i = 0; i < numberOfSections; i++)
            {
                fs.Position += 8;                         // Name[8]
                br.ReadUInt32();                          // VirtualSize
                uint va = br.ReadUInt32();                // VirtualAddress
                uint rawSize = br.ReadUInt32();           // SizeOfRawData
                uint rawPtr = br.ReadUInt32();            // PointerToRawData
                fs.Position += 16;                        // 残り16バイト
                sections[i] = (va, rawPtr, rawSize);
            }

            // RVA → ファイルオフセット変換
            long RvaToFileOffset(uint rva)
            {
                foreach (var (va, ptr, size) in sections)
                    if (rva >= va && rva < va + size)
                        return ptr + (rva - va);
                return -1;
            }

            // --- インポートディレクトリテーブルを読む ---
            // IMAGE_IMPORT_DESCRIPTOR (各20 bytes)
            long importTableOffset = RvaToFileOffset(importDirRva);
            if (importTableOffset < 0) return result;

            fs.Position = importTableOffset;

            while (true)
            {
                br.ReadUInt32(); // OriginalFirstThunk
                br.ReadUInt32(); // TimeDateStamp
                br.ReadUInt32(); // ForwarderChain
                uint nameRva = br.ReadUInt32();
                br.ReadUInt32(); // FirstThunk

                if (nameRva == 0) break; // ターミネーター

                long nameOffset = RvaToFileOffset(nameRva);
                if (nameOffset < 0) break;

                long savedPos = fs.Position;
                fs.Position = nameOffset;

                // null終端のDLL名を読む
                var nameBytes = new StringBuilder(64);
                int b;
                while ((b = fs.ReadByte()) > 0)
                    nameBytes.Append((char)b);

                string dllName = nameBytes.ToString();
                if (!string.IsNullOrWhiteSpace(dllName))
                    result.Add(dllName);

                fs.Position = savedPos;
            }
        }
        catch (Exception)
        {
            // 解析失敗は無視 - GXWorks3の起動には影響しない
        }

        return result;
    }
}
