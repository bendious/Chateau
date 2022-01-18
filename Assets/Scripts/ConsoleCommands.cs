using UnityEngine;


public /*static*/ class ConsoleCommands : MonoBehaviour
{
	public static bool PassiveAI { get; private set; }

	public enum AIDebugLevels
	{
		None,
		State,
		Path,
	}
	public static int AIDebugLevel { get; private set; }


	private /*readonly*/ GameController m_gameController;


	private void Start()
	{
		m_gameController = Camera.main.GetComponent<GameController>();
	}

#if DEBUG
	// TODO: callback rather than checking every frame?
	private void Update()
	{
		if (!Input.GetKey(KeyCode.BackQuote))
		{
			return;
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
			m_gameController.SpawnEnemyWave();
		}

		if (Input.GetKeyDown(KeyCode.K))
		{
			m_gameController.DebugKillAllEnemies();
		}
	}
#endif
}
