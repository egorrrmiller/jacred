namespace JacRed.Core.Models;

public class TaskParse
{
	public DateTime updateTime { get; set; }

	public int page { get; set; }

	#region TaskParse

	public TaskParse()
	{
	}

	public TaskParse(int _page) => page = _page;

	#endregion
}