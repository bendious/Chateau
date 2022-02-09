using UnityEngine;


public /*static*/ class ConsoleCommands : MonoBehaviour
{
	public GameObject[] m_visualizationPrefabs;


	public static bool NeverDie { get; private set; }

	public static bool PassiveAI { get; private set; }

	public enum AIDebugLevels
	{
		None,
		State,
		Path,
	}
	public static int AIDebugLevel { get; private set; }

	public static bool RegenerateDisabled { get; private set; }


	private ConsoleControls m_controls;

	private int m_controlsVisualizationIdx = -1;
	private GameObject m_controlsVisualization;


#if DEBUG
	private void Awake()
	{
		m_controls = new();
	}

	private void OnDisable()
	{
		m_controls.Disable();
	}

	private void OnEnable()
	{
		m_controls.Enable();
	}

	// TODO: callback rather than checking every frame?
	private void Update()
	{
		ConsoleControls.ConsoleActions consoleControls = m_controls.Console;

#if UNITY_EDITOR
		// TODO: detect even while editor is paused?
		if (consoleControls.Pause.triggered) // NOTE that KeyCode.Pause is the 'Pause/Break' keyboard key, not Esc
		{
			UnityEditor.EditorApplication.isPaused = !UnityEditor.EditorApplication.isPaused;
		}
#endif

		if (!consoleControls.Toggle.IsPressed())
		{
			return;
		}

		if (consoleControls.NeverDie.triggered)
		{
			NeverDie = !NeverDie;
		}

		if (consoleControls.PassiveAI.triggered)
		{
			PassiveAI = !PassiveAI;
		}

		if (consoleControls.AIDebugLevel.triggered)
		{
			AIDebugLevel = (AIDebugLevel + 1) % Utility.EnumNumTypes<AIDebugLevels>();
		}

		if (consoleControls.RegenerateDisabled.triggered)
		{
			RegenerateDisabled = !RegenerateDisabled;
		}

		if (consoleControls.SpawnEnemyWave.triggered)
		{
			GameController.Instance.SpawnEnemyWave();
		}

		if (consoleControls.KillAllEnemies.triggered)
		{
			GameController.Instance.DebugKillAllEnemies();
		}

		if (consoleControls.HarmHealAvatar.triggered)
		{
			Health avatarHealth = GameController.Instance.m_avatar.GetComponent<Health>();
			if (consoleControls.Shift.IsPressed())
			{
				avatarHealth.Increment();
			}
			else
			{
				avatarHealth.Decrement(gameObject);
			}
		}

		if (consoleControls.SpawnEnemy.triggered)
		{
			GameController.Instance.DebugSpawnEnemy(Mathf.RoundToInt(consoleControls.SpawnEnemy.ReadValue<float>()) % 10);
		}

		if (consoleControls.ControlsVisualization.triggered)
		{
			if (m_controlsVisualization != null)
			{
				Simulation.Schedule<ObjectDespawn>().m_object = m_controlsVisualization;
			}

			++m_controlsVisualizationIdx;
			if (m_controlsVisualizationIdx < m_visualizationPrefabs.Length)
			{
				m_controlsVisualization = Instantiate(m_visualizationPrefabs[m_controlsVisualizationIdx]);
			}
			else
			{
				m_controlsVisualization = null;
				m_controlsVisualizationIdx = -1;
			}
		}
	}
#endif
}
