using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Dm.Config;
using Dm.util;

namespace Dm;

public class FldrErrorWriter : BaseFlusher
{
	protected static CacheQueue<byte[]> flushQueue = new CacheQueue<byte[]>(1000, enableLRU: false);

	private const int ONE_MIN_MS = 60000;

	protected static int maxFileNumber = 5;

	protected static int switchMode = 2;

	protected static int switchLimit = 128;

	private const int SWITCH_MODE_OFF = 0;

	private const int SWITCH_MODE_BY_LINE_COUNT = 1;

	private const int SWITCH_MODE_BY_FILE_SIZE = 2;

	private int count;

	private bool stopFlag;

	protected long lineCount;

	protected DateTime lastTime = DateTime.Now;

	private string realFileName;

	private bool customFile;

	private readonly int maxErrorNum;

	public object lockObject = new object();

	private FldrErrorWriter(int maxErrorNum, string logDir)
		: base("FldrLogFlusher", DmSvcConfig.logDir, "dm_fldr", DmSvcConfig.logFlushFreq)
	{
		if (StringUtil.isNotEmpty(logDir))
		{
			filePath = logDir;
			customFile = true;
		}
		maxFileNumber = 1;
		switchMode = 0;
		this.maxErrorNum = maxErrorNum;
		start();
	}

	private void start()
	{
		_basethread.Start();
	}

	public static FldrErrorWriter getInstance(int maxErrorNum, string logDir)
	{
		return new FldrErrorWriter(maxErrorNum, logDir);
	}

	public void setStopFlag(bool stopFlag)
	{
		this.stopFlag = stopFlag;
	}

	protected override void DoRun()
	{
		while (flushFreq > 0 && !stopFlag)
		{
			byte[] bytes = flushQueue.get();
			flushProcess(bytes);
		}
	}

	protected override void BeforeExit()
	{
		byte[] bytes = null;
		do
		{
			flushProcess(bytes);
		}
		while ((bytes = flushQueue.get()) != null);
	}

	public void write(string msg)
	{
		lock (lockObject)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(msg);
			flushQueue.put(bytes);
		}
	}

	public void writeLine(string msg)
	{
		lock (lockObject)
		{
			if (maxErrorNum > 0 && count >= maxErrorNum)
			{
				stopFlag = true;
				return;
			}
			byte[] bytes = Encoding.UTF8.GetBytes(StringUtil.trimToEmpty(msg) + StringUtil.LINE_SEPARATOR);
			flushQueue.put(bytes);
		}
	}

	public void writeLines(params string[] msgList)
	{
		if (msgList == null || msgList.Length == 0)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < msgList.Length; i++)
		{
			if (i == 1)
			{
				stringBuilder.Append("[REASON]");
			}
			stringBuilder.Append(msgList[i]).Append(StringUtil.LINE_SEPARATOR);
		}
		writeLine(stringBuilder.ToString());
		count++;
	}

	protected override BufferedStream createNewFile()
	{
		if (switchMode != 0)
		{
			deleteOldFile();
		}
		try
		{
			DateTime now = DateTime.Now;
			dateString = DateUtil.formatDate(now, 1);
			if (realFileName == null || switchMode != 0)
			{
				realFileName = filePrefix + "_" + DateUtil.formatDate(now, 3) + "_" + fileNum + fileExtension;
			}
			if (customFile)
			{
				if (StringUtil.isEmpty(filePath))
				{
					filePath = realFileName;
				}
				logFile = new FileInfo(filePath);
			}
			else if (StringUtil.isNotEmpty(filePath))
			{
				DirectoryInfo directoryInfo = new DirectoryInfo(filePath);
				if (!directoryInfo.Exists)
				{
					directoryInfo.Create();
				}
				logFile = new FileInfo(filePath + realFileName);
			}
			if (!logFile.Exists)
			{
				fileNum++;
				return new BufferedStream(new FileStream(logFile.FullName, FileMode.CreateNew), DmSvcConfig.logSize);
			}
			return new BufferedStream(new FileStream(logFile.FullName, FileMode.Append), DmSvcConfig.logSize);
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.StackTrace);
		}
		return null;
	}

	private void deleteOldFile()
	{
		FileInfo[] files = getFiles();
		if (files.Length < maxFileNumber)
		{
			return;
		}
		for (int i = 0; i <= files.Length - maxFileNumber; i++)
		{
			int num = 0;
			do
			{
				try
				{
					files[i].Delete();
				}
				catch (Exception)
				{
					num++;
					continue;
				}
				break;
			}
			while (num != 3);
		}
		fileNum %= int.MaxValue;
	}

	private FileInfo[] getFiles()
	{
		return (from file in Directory.GetFiles(filePath)
			where Path.GetFileName(file).StartsWith(filePrefix) && Path.GetFileName(file).Contains(fileExtension)
			select new FileInfo(file)).ToArray();
	}

	private void flushProcess(byte[] bytes)
	{
		lock (lockObject)
		{
			if (bytes != null)
			{
				buffer.putBytes(bytes, 0, bytes.Length);
				if (switchMode == 1)
				{
					Interlocked.Increment(ref lineCount);
				}
			}
			if (switchMode == 1 && Interlocked.Read(in lineCount) == switchLimit)
			{
				doFlush(buffer);
				Interlocked.Exchange(ref lineCount, 0L);
			}
			if ((bytes == null && buffer.length() > 0) || buffer.length() >= DmSvcConfig.logBufferSize)
			{
				doFlush(buffer);
			}
		}
	}

	protected override bool needCreateNewFile()
	{
		if (switchMode == 0)
		{
			return logFile == null;
		}
		if (switchMode == 1)
		{
			if (logFile != null)
			{
				return switchLimit == Interlocked.Read(in lineCount);
			}
			return true;
		}
		if (switchMode == 2)
		{
			if (logFile != null)
			{
				return logFile.Length >= (long)switchLimit * 1024L * 1024;
			}
			return true;
		}
		DateTime now = DateTime.Now;
		bool flag = (now - lastTime).TotalMilliseconds >= (double)(60000L * (long)switchLimit);
		if (flag)
		{
			lastTime = now;
		}
		return logFile == null || flag;
	}
}
