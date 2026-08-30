using System;
using System.IO;

namespace PLCCompare
{
    public class DataLogger
    {
        private readonly string mainFolder;
        private readonly string subFolder;
        private DateTime currentDate;
        private StreamWriter output;

        public DataLogger(string mainFolder, string subFolder)
        {
            this.mainFolder = mainFolder;
            this.subFolder = subFolder;
            NewFileByDate();
        }

        public void NewFileByDate()
        {
            CloseCurrentOutput();

            currentDate = DateTime.Now.Date;
            string folderPath = Path.Combine(mainFolder, subFolder);

            // Equivalent of Java's folder.mkdirs() — creates the folder (and any missing
            // parent folders) if it doesn't already exist.
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string fileName = Path.Combine(folderPath, currentDate.ToString("yyyy-MM-dd") + ".txt");
            bool isNewFile = !File.Exists(fileName);

            try
            {
                // append: true — same behavior as Java's new FileWriter(fileName, true)
                output = new StreamWriter(fileName, append: true);

                if (isNewFile)
                {
                    output.WriteLine(string.Format(
                        "{0,-20}{1,-15}{2,-30}{3,-30}{4,-10}",
                        "Date", "Time", "Batch No", "Rank", "Status"));
                    output.Flush();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to open log file: " + ex.Message);
            }
        }

        public void LogComparison(string batchNo, string scannedValue, string matchStatus)
        {
            if (DateTime.Now.Date != currentDate)
            {
                NewFileByDate();
            }

            if (output == null)
            {
                return;
            }

            string dateStr = DateTime.Now.ToString("dd / MM / yy");
            string timeStr = DateTime.Now.ToString("HH:mm:ss");

            output.WriteLine(string.Format(
                "{0,-20}{1,-15}{2,-30}{3,-30}{4,-10}",
                dateStr, timeStr, batchNo, scannedValue, matchStatus));
            output.Flush();
        }

        private void CloseCurrentOutput()
        {
            // "?." is C#'s null-conditional operator — equivalent to Java's
            // "if (output != null) { output.Close(); }" in a single line.
            output?.Close();
        }

        public void Close()
        {
            CloseCurrentOutput();
        }
    }
}