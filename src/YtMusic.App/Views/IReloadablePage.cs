using System.Threading.Tasks;

namespace YtMusic.App.Views;

public interface IReloadablePage
{
	Task ReloadAsync();
}
