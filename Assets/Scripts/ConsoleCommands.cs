using UnityEngine;


public /*static*/ class ConsoleCommands : MonoBehaviour
{
	public static bool NeverDie { get; private set; }

	public static bool PassiveAI { get; private set; }

	public enum AIDebugLevels
	{
		None,
		State,
		Path,
	}
	public static int AIDebugLevel { get; private set; }


#if DEBUG
	// TODO: callback rather than checking every frame?
	private void Update()
	{
		if (!Input.GetKey(KeyCode.BackQuote))
		{
			return;
		}

		if (Input.GetKeyDown(KeyCode.N))
		{
			NeverDie = !NeverDie;
		}

		if (Input.GetKeyDown(KeyCode.A))
		{
			PassiveAI = !PassiveAI;
		}

		if (Input.GetKeyDown(KeyCode.D))
		{
			AIDebugLevel = (AIDebugLevel + 1) % Utility.EnumNumTypes<AIDebugLevels>();
		}

		if (Input.GetKeyDown(KeyCode.W))
		{
			GameController.Instance.SpawnEnemyWave();
		}

		if (Input.GetKeyDown(KeyCode.K))
		{
			GameController.Instance.DebugKillAllEnemies();
		}

		// TODO: cleaner way of listening for any number key?
		bool key0 = Input.GetKeyDown(KeyCode.Alpha0);
		bool key1 = Input.GetKeyDown(KeyCode.Alpha1);
		bool key2 = Input.GetKeyDown(KeyCode.Alpha2);
		bool key3 = Input.GetKeyDown(KeyCode.Alpha3);
		bool key4 = Input.GetKeyDown(KeyCode.Alpha4);
		bool key5 = Input.GetKeyDown(KeyCode.Alpha5);
		bool key6 = Input.GetKeyDown(KeyCode.Alpha6);
		bool key7 = Input.GetKeyDown(KeyCode.Alpha7);
		bool key8 = Input.GetKeyDown(KeyCode.Alpha8);
		bool key9 = Input.GetKeyDown(KeyCode.Alpha9);
		if (key0 || key1 || key2 || key3 || key4 || key5 || key6 || key7 || key8 || key9)
		{
			GameController.Instance.DebugSpawnEnemy(/*(key0 ? 0 : 0) +*/ (key1 ? 1 : 0) + (key2 ? 2 : 0) + (key3 ? 3 : 0) + (key4 ? 4 : 0) + (key5 ? 5 : 0) + (key6 ? 6 : 0) + (key7 ? 7 : 0) + (key8 ? 8 : 0) + (key9 ? 9 : 0));
		}
	}
#endif
}
