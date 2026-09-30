using System;
using System.Threading;
using W.Dm.util;

namespace W.Dm.filter.fldr;

public class FldrTask
{
	public delegate void doTaskDelegate();

	private bool success;

	private Exception error;

	private CountdownEvent countDownLatch;

	private FldrStatement statement;

	private FldrErrorWriter fldrErrorWriter;

	public doTaskDelegate doTask;

	public FldrTask(CountdownEvent countDownLatch)
	{
		this.countDownLatch = countDownLatch;
	}

	public FldrTask()
	{
	}

	public FldrTask(FldrStatement statement, FldrErrorWriter fldrErrorWriter)
	{
		this.statement = statement;
		this.fldrErrorWriter = fldrErrorWriter;
	}

	public void run()
	{
		try
		{
			doTask();
			setSuccess(success: true);
		}
		catch (ThreadInterruptedException ex)
		{
			setError(new Exception(ex.Message));
			throw;
		}
		catch (Exception ex2)
		{
			setError(ex2);
			setSuccess(success: false);
		}
		finally
		{
			if (statement != null && error != null)
			{
				fldrErrorWriter.writeLines(statement.schemaTable, error.Message, StringUtil.LINE_SEPARATOR);
			}
			if (countDownLatch != null)
			{
				countDownLatch.Signal();
			}
		}
	}

	public void setSuccess(bool success)
	{
		this.success = success;
	}

	public bool isSuccess()
	{
		return success;
	}

	public void setError(Exception error)
	{
		this.error = error;
	}

	public Exception getError()
	{
		return error;
	}
}
