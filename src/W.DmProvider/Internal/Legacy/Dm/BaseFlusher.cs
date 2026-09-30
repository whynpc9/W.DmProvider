using System;
using System.IO;
using W.Dm.Config;
using W.Dm.util;

namespace W.Dm;

public class BaseFlusher : BaseThread
{
	protected string dateString = DateUtil.formatDate(DateTime.Now, 1);

	protected FileInfo logFile;

	protected BufferedStream _output;

	protected string filePrefix;

	protected int flushFreq;

	protected string filePath;

	public const int MAX_FILE_SIZE = 104857600;

	public const int FLUSH_SIZE = 32768;

	protected ByteArrayQueue buffer = new ByteArrayQueue();

	protected string fileExtension = ".log";

	protected int fileNum;

	public BaseFlusher(string threadName, string filePath, string filePrefix, int flushFreq)
		: base(threadName)
	{
		this.filePath = filePath;
		this.filePrefix = filePrefix;
		this.flushFreq = flushFreq;
	}

	protected virtual void DoRun()
	{
	}

	public override void Run()
	{
		try
		{
			DoRun();
		}
		finally
		{
			try
			{
				BeforeExit();
			}
			catch (Exception)
			{
			}
			closeCurrentFile();
		}
	}

	protected void doFlush(ByteArrayQueue buffer)
	{
		if (_output == null || needCreateNewFile())
		{
			closeCurrentFile();
			_output = createNewFile();
		}
		try
		{
			buffer.writeBytes(_output, buffer.length());
			_output.Flush();
		}
		catch (IOException)
		{
			_output.Close();
			_output = null;
		}
		finally
		{
			buffer.clear();
		}
	}

	protected virtual BufferedStream createNewFile()
	{
		try
		{
			DateTime now = DateTime.Now;
			string str = DateUtil.formatDate(now, 1);
			if (!StringUtil.Equals(str, dateString))
			{
				fileNum = 0;
			}
			dateString = str;
			string text = filePrefix + "_" + DateUtil.formatDate(now, 3) + "_" + fileNum + fileExtension;
			if (StringUtil.isNotEmpty(filePath) && StringUtil.isNotEmpty(text))
			{
				DirectoryInfo directoryInfo = new DirectoryInfo(filePath);
				if (!directoryInfo.Exists)
				{
					directoryInfo.Create();
				}
				logFile = new FileInfo(filePath + text);
				fileNum++;
				if (!logFile.Exists)
				{
					return new BufferedStream(new FileStream(logFile.FullName, FileMode.CreateNew), DmSvcConfig.logSize);
				}
				return new BufferedStream(new FileStream(logFile.FullName, FileMode.Append), DmSvcConfig.logSize);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.StackTrace);
		}
		return null;
	}

	protected void closeCurrentFile()
	{
		if (_output == null)
		{
			return;
		}
		try
		{
			_output.Close();
		}
		catch (IOException)
		{
		}
		finally
		{
			_output = null;
		}
	}

	protected virtual bool needCreateNewFile()
	{
		if (DateUtil.formatDate(DateTime.Now, 1).Equals(dateString) && logFile != null)
		{
			return logFile.Length > 104857600;
		}
		return true;
	}
}
