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

	public static bool RegenerateDisabled { get; private set; }


	private ConsoleControls m_controls;


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
#if UNITY_EDITOR
		// TODO: detect even while editor is paused?
		if (m_controls.Console.Pause.triggered) // NOTE that KeyCode.Pause is the 'Pause/Break' keyboard key, not Esc
		{
			UnityEditor.EditorApplication.isPaused = !UnityEditor.EditorApplication.isPaused;
		}
#endif

		if (!m_controls.Console.Toggle.IsPressed())
		{
			return;
		}

		if (m_controls.Console.NeverDie.triggered)
		{
			NeverDie = !NeverDie;
		}

		if (m_controls.Console.PassiveAI.triggered)
		{
			PassiveAI = !PassiveAI;
		}

		if (m_controls.Console.AIDebugLevel.triggered)
		{
			AIDebugLevel = (AIDebugLevel + 1) % Utility.EnumNumTypes<AIDebugLevels>();
		}

		if (m_controls.Console.RegenerateDisabled.triggered)
		{
			RegenerateDisabled = !RegenerateDisabled;
		}

		if (m_controls.Console.SpawnEnemyWave.triggered)
		{
			GameController.Instance.SpawnEnemyWave();
		}

		if (m_controls.Console.KillAllEnemies.triggered)
		{
			GameController.Instance.DebugKillAllEnemies();
		}

		if (m_controls.Console.HarmHealAvatar.triggered)
		{
			Health avatarHealth = GameController.Instance.m_avatar.GetComponent<Health>();
			if (m_controls.Console.Shift.IsPressed())
			{
				avatarHealth.Increment();
			}
			else
			{
				avatarHealth.Decrement(gameObject);
			}
		}

		// TODO: cleaner way of listening for any number key?
		if (m_controls.Console.SpawnEnemy.triggered)
		{
			GameController.Instance.DebugSpawnEnemy(Mathf.RoundToInt(m_controls.Console.SpawnEnemy.ReadValue<float>()) % 10);
		}
	}
#endif
}
