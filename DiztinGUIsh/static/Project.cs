﻿using DiztinGUIsh.window;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiztinGUIsh
{
    public static class Project
    {
        public const int HEADER_SIZE = 0x100;

        public static string currentFile = null, currentROMFile = null;
        public static bool unsavedChanges = false;
        public static string watermark = "DiztinGUIsh";

        public static bool NewProject(string filename)
        {
            try
            {
                byte[] smc = File.ReadAllBytes(filename);
                byte[] rom = new byte[smc.Length & 0x7FFFFC00];

                if ((smc.Length & 0x3FF) == 0x200) for (int i = 0; i < rom.Length; i++) rom[i] = smc[i + 0x200];
                else if ((smc.Length & 0x3FF) != 0) throw new Exception("This ROM has an unusual size. It can't be opened.");
                else rom = smc;

                if (rom.Length < 0x8000) throw new Exception("This ROM is too small. It can't be opened.");

                currentROMFile = filename;

                ImportROMDialog import = new ImportROMDialog(rom);
                DialogResult result = import.ShowDialog();
                if (result == DialogResult.OK)
                {
                    Data.Initiate(rom, import.GetROMMapMode(), import.GetROMSpeed());
                    unsavedChanges = false;
                    currentFile = null;

                    AliasList.me.Reset();
                    Dictionary<int, string> generatedLabels = import.GetGeneratedLabels();
                    if (generatedLabels.Count > 0)
                    {
                        foreach (KeyValuePair<int, string> pair in generatedLabels) Data.AddLabel(pair.Key, pair.Value, true);
                        unsavedChanges = true;
                    }

                    Dictionary<int, Data.FlagType> generatedFlags = import.GetHeaderFlags();
                    if (generatedFlags.Count > 0)
                    {
                        foreach (KeyValuePair<int, Data.FlagType> pair in generatedFlags) Data.SetFlag(pair.Key, pair.Value);
                        unsavedChanges = true;
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        public static void SaveProject(string filename)
        {
            try
            {
                byte[] data = SaveVersion1();
                byte[] everything = new byte[HEADER_SIZE + data.Length];
                everything[0] = 1; // version
                Util.StringToByteArray(watermark).CopyTo(everything, 1);
                data.CopyTo(everything, HEADER_SIZE);

                if (!IsUncompressedProject(filename)) everything = TryZip(everything);

                File.WriteAllBytes(filename, everything);
                unsavedChanges = false;
                currentFile = filename;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static byte[] SaveVersion1()
        {
            int size = Data.GetROMSize();
            byte[] romSettings = new byte[31];
            romSettings[0] = (byte)Data.GetROMMapMode();
            romSettings[1] = (byte)Data.GetROMSpeed();
            Util.IntegerIntoByteArray(size, romSettings, 2);
            for (int i = 0; i < 0x15; i++) romSettings[6 + i] = (byte)Data.GetROMByte(Util.ConvertSNEStoPC(0xFFC0 + i));
            for (int i = 0; i < 4; i++) romSettings[27 + i] = (byte)Data.GetROMByte(Util.ConvertSNEStoPC(0xFFDC + i));

            // TODO put selected offset in save file

            List<byte> label = new List<byte>(), comment = new List<byte>();
            Dictionary<int, string> all_labels = Data.GetAllLabels(), all_comments = Data.GetAllComments();

            Util.IntegerIntoByteList(all_labels.Count, label);
            foreach (KeyValuePair<int, string> pair in all_labels)
            {
                Util.IntegerIntoByteList(pair.Key, label);
                for (int i = 0; i < pair.Value.Length; i++) label.Add((byte)pair.Value[i]);
                label.Add(0);
            }

            Util.IntegerIntoByteList(all_comments.Count, comment);
            foreach (KeyValuePair<int, string> pair in all_comments)
            {
                Util.IntegerIntoByteList(pair.Key, comment);
                for (int i = 0; i < pair.Value.Length; i++) comment.Add((byte)pair.Value[i]);
                comment.Add(0);
            }

            byte[] romLocation = Util.StringToByteArray(currentROMFile);

            byte[] data = new byte[romSettings.Length + romLocation.Length + 8 * size + label.Count + comment.Count];
            romSettings.CopyTo(data, 0);
            for (int i = 0; i < romLocation.Length; i++) data[romSettings.Length + i] = romLocation[i];
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + i] = (byte)Data.GetDataBank(i);
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + size + i] = (byte)Data.GetDirectPage(i);
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + 2 * size + i] = (byte)(Data.GetDirectPage(i) >> 8);
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + 3 * size + i] = (byte)(Data.GetXFlag(i) ? 1 : 0);
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + 4 * size + i] = (byte)(Data.GetMFlag(i) ? 1 : 0);
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + 5 * size + i] = (byte)Data.GetFlag(i);
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + 6 * size + i] = (byte)Data.GetArchitechture(i);
            for (int i = 0; i < size; i++) data[romSettings.Length + romLocation.Length + 7 * size + i] = (byte)Data.GetInOutPoint(i);
            // ???
            label.CopyTo(data, romSettings.Length + romLocation.Length + 8 * size);
            comment.CopyTo(data, romSettings.Length + romLocation.Length + 8 * size + label.Count);
            // ???

            return data;
        }

        public static bool TryOpenProject(string filename, OpenFileDialog open)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filename);

                if (!IsUncompressedProject(filename)) data = TryUnzip(data);

                for (int i = 0; i < watermark.Length; i++)
                {
                    if (data[i + 1] != (byte)watermark[i])
                    {
                        throw new Exception("This is not a valid DiztinGUIsh file!");
                    }
                }

                byte version = data[0];

                switch (version)
                {
                    case 0: OpenVersion0(data, open); break;
                    case 1: OpenVersion1(data, open); break;
                    default: throw new Exception("This DiztinGUIsh file uses a newer file format! You'll need to download the newest version of DiztinGUIsh to open it.");
                }

                unsavedChanges = false;
                currentFile = filename;
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        // differences between version 0 and version 1:
        // version 0: addresses for aliases and comments were stored in PC offset format.
        //            tables: B, D lo, D hi, X, M, flag, arch, inoutpoint
        //            lists: alias, comment
        // version 1: addresses for aliases and comments are stored in SNES address format.
        //            tables: B, D lo, D hi, X, M, flag, arch, inoutpoint, ???
        //            lists: alias, comment, ???
        private static void OpenVersion0(byte[] unzipped, OpenFileDialog open)
        {
            MessageBox.Show(
                "This project file is in an older format.\n" +
                "You may want to back up your work or 'Save As' in case the conversion goes wrong.\n" +
                "The project file will be untouched until it is saved again.",
                "Project File Out of Date", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

            Data.ROMMapMode mode = (Data.ROMMapMode)unzipped[HEADER_SIZE];
            Data.ROMSpeed speed = (Data.ROMSpeed)unzipped[HEADER_SIZE + 1];
            int size = Util.ByteArrayToInteger(unzipped, HEADER_SIZE + 2);
            string romName = "", romLocation = "";
            byte[] rom;

            int pointer = HEADER_SIZE + 6;
            for (int i = 0; i < 0x15; i++) romName += (char)unzipped[pointer++];
            int checksums = Util.ByteArrayToInteger(unzipped, pointer);
            pointer += 4;
            while (unzipped[pointer] != 0) romLocation += (char)unzipped[pointer++];
            pointer++;

            if (ValidateROM(romLocation, romName, checksums, mode, out rom, open))
            {
                Data.Initiate(rom, mode, speed);

                for (int i = 0; i < size; i++) Data.SetDataBank(i, unzipped[pointer + i]);
                for (int i = 0; i < size; i++) Data.SetDirectPage(i, unzipped[pointer + size + i] | (unzipped[pointer + 2 * size + i] << 8));
                for (int i = 0; i < size; i++) Data.SetXFlag(i, unzipped[pointer + 3 * size + i] != 0);
                for (int i = 0; i < size; i++) Data.SetMFlag(i, unzipped[pointer + 4 * size + i] != 0);
                for (int i = 0; i < size; i++) Data.SetFlag(i, (Data.FlagType)unzipped[pointer + 5 * size + i]);
                for (int i = 0; i < size; i++) Data.SetArchitechture(i, (Data.Architechture)unzipped[pointer + 6 * size + i]);
                for (int i = 0; i < size; i++) Data.SetInOutPoint(i, (Data.InOutPoint)unzipped[pointer + 7 * size + i]);

                pointer += 8 * size;
                int label_count = Util.ByteArrayToInteger(unzipped, pointer);
                pointer += 4;

                AliasList.me.Reset();
                for (int i = 0; i < label_count; i++)
                {
                    int offset = Util.ConvertPCtoSNES(Util.ByteArrayToInteger(unzipped, pointer)); // pc -> snes
                    pointer += 4;

                    string label = "";
                    while (unzipped[pointer] != 0) label += (char)unzipped[pointer++];
                    pointer++;

                    Data.AddLabel(offset, label, true);
                }

                int comment_count = Util.ByteArrayToInteger(unzipped, pointer);
                pointer += 4;

                for (int i = 0; i < comment_count; i++)
                {
                    int offset = Util.ConvertPCtoSNES(Util.ByteArrayToInteger(unzipped, pointer)); // pc -> snes
                    pointer += 4;

                    string comment = "";
                    while (unzipped[pointer] != 0) comment += (char)unzipped[pointer++];
                    pointer++;

                    Data.AddComment(offset, comment, true);
                }
            }
            else
            {
                throw new Exception("Couldn't open the ROM file!");
            }
        }

        private static void OpenVersion1(byte[] unzipped, OpenFileDialog open)
        {
            Data.ROMMapMode mode = (Data.ROMMapMode)unzipped[HEADER_SIZE];
            Data.ROMSpeed speed = (Data.ROMSpeed)unzipped[HEADER_SIZE + 1];
            int size = Util.ByteArrayToInteger(unzipped, HEADER_SIZE + 2);
            string romName = "", romLocation = "";
            byte[] rom;

            int pointer = HEADER_SIZE + 6;
            for (int i = 0; i < 0x15; i++) romName += (char)unzipped[pointer++];
            int checksums = Util.ByteArrayToInteger(unzipped, pointer);
            pointer += 4;
            while (unzipped[pointer] != 0) romLocation += (char)unzipped[pointer++];
            pointer++;

            if (ValidateROM(romLocation, romName, checksums, mode, out rom, open))
            {
                Data.Initiate(rom, mode, speed);

                for (int i = 0; i < size; i++) Data.SetDataBank(i, unzipped[pointer + i]);
                for (int i = 0; i < size; i++) Data.SetDirectPage(i, unzipped[pointer + size + i] | (unzipped[pointer + 2 * size + i] << 8));
                for (int i = 0; i < size; i++) Data.SetXFlag(i, unzipped[pointer + 3 * size + i] != 0);
                for (int i = 0; i < size; i++) Data.SetMFlag(i, unzipped[pointer + 4 * size + i] != 0);
                for (int i = 0; i < size; i++) Data.SetFlag(i, (Data.FlagType)unzipped[pointer + 5 * size + i]);
                for (int i = 0; i < size; i++) Data.SetArchitechture(i, (Data.Architechture)unzipped[pointer + 6 * size + i]);
                for (int i = 0; i < size; i++) Data.SetInOutPoint(i, (Data.InOutPoint)unzipped[pointer + 7 * size + i]);

                pointer += 8 * size;
                int label_count = Util.ByteArrayToInteger(unzipped, pointer);
                pointer += 4;

                AliasList.me.Reset();
                for (int i = 0; i < label_count; i++)
                {
                    int offset = Util.ByteArrayToInteger(unzipped, pointer);
                    pointer += 4;

                    string label = "";
                    while (unzipped[pointer] != 0) label += (char)unzipped[pointer++];
                    pointer++;

                    Data.AddLabel(offset, label, true);
                }

                int comment_count = Util.ByteArrayToInteger(unzipped, pointer);
                pointer += 4;

                for (int i = 0; i < comment_count; i++)
                {
                    int offset = Util.ByteArrayToInteger(unzipped, pointer);
                    pointer += 4;

                    string comment = "";
                    while (unzipped[pointer] != 0) comment += (char)unzipped[pointer++];
                    pointer++;

                    Data.AddComment(offset, comment, true);
                }
            }
            else
            {
                throw new Exception("Couldn't open the ROM file!");
            }
        }

        private static bool ValidateROM(string filename, string romName, int checksums, Data.ROMMapMode mode, out byte[] rom, OpenFileDialog open)
        {
            bool validFile = false, matchingROM = false;
            rom = null;
            open.InitialDirectory = currentFile;

            while (!matchingROM)
            {
                string error = null;
                matchingROM = false;

                while (!validFile)
                {
                    error = null;
                    validFile = false;

                    try
                    {
                        byte[] smc = File.ReadAllBytes(filename);
                        rom = new byte[smc.Length & 0x7FFFFC00];

                        if ((smc.Length & 0x3FF) == 0x200) for (int i = 0; i < rom.Length; i++) rom[i] = smc[i + 0x200];
                        else if ((smc.Length & 0x3FF) != 0) error = "The linked ROM has an unusual size. It can't be opened.";
                        else rom = smc;

                        if (error == null) validFile = true;
                    }
                    catch (Exception)
                    {
                        error = string.Format("The linked ROM file '{0}' couldn't be found.", filename);
                    }

                    if (!validFile)
                    {
                        DialogResult result = MessageBox.Show(string.Format("{0} Link a new ROM now?", error), "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                        if (result == DialogResult.No) return false;
                        result = open.ShowDialog();
                        if (result == DialogResult.OK) filename = open.FileName;
                        else return false;
                    }
                }

                validFile = false;
                int offset = Data.GetRomSettingOffset(mode);
                if (rom.Length <= offset + 10) error = "The linked ROM is too small. It can't be opened.";

                string myName = "";
                for (int i = 0; i < 0x15; i++) myName += (char)rom[offset - 0x15 + i];
                int myChecksums = Util.ByteArrayToInteger(rom, offset + 7);

                if (myName != romName) error = string.Format("The linked ROM's internal name '{0}' doesn't match the project's internal name of '{1}'.", myName, romName);
                else if (checksums != myChecksums) error = string.Format("The linked ROM's checksums '{0:X8}' don't match the project's checksums of '{1:X8}'.", myChecksums, checksums);

                if (error == null) matchingROM = true;

                if (!matchingROM)
                {
                    DialogResult result = MessageBox.Show(string.Format("{0} Link a new ROM now?", error), "Error", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                    if (result == DialogResult.No) return false;
                    result = open.ShowDialog();
                    if (result == DialogResult.OK) filename = open.FileName;
                    else return false;
                }
            }

            if (currentROMFile != filename)
            {
                currentROMFile = filename;
                unsavedChanges = true;
            }
            return true;
        }

        private static bool IsUncompressedProject(string filename)
        {
            return Path.GetExtension(filename).Equals(".dizraw", StringComparison.InvariantCultureIgnoreCase);
        }

        // https://stackoverflow.com/questions/33119119/unzip-byte-array-in-c-sharp
        private static byte[] TryUnzip(byte[] data)
        {
            try
            {
                using (MemoryStream comp = new MemoryStream(data))
                using (GZipStream gzip = new GZipStream(comp, CompressionMode.Decompress))
                using (MemoryStream res = new MemoryStream())
                {
                    gzip.CopyTo(res);
                    return res.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static byte[] TryZip(byte[] data)
        {
            try
            {
                using (MemoryStream comp = new MemoryStream())
                using (GZipStream gzip = new GZipStream(comp, CompressionMode.Compress))
                {
                    gzip.Write(data, 0, data.Length);
                    gzip.Close();
                    return comp.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
