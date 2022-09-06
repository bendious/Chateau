#if DEBUG
using System.Linq;
#endif
using UnityEngine;


[DisallowMultipleComponent]
public /*static*/ class ConsoleCommands : MonoBehaviour
{
	public /*static*/ GameObject[] m_visualizationPrefabs;


	public static bool TimerPaused { get; private set; }

	public static bool NeverDie { get; private set; }

	public static bool PassiveAI { get; private set; }

	public enum AIDebugLevels
	{
		None,
		State,
		Positions,
	}
	public static int AIDebugLevel { get; private set; }

	public enum ItemDebugLevels
	{
		None,
		Deactivated,
		Supported,
		Ignored,
		Static,
		Attach,
		TooSlow,
		Damage,
	}
	public static int ItemDebugLevel { get; private set; }

	public static bool ExplosionDebug { get; private set; }

	public enum LayoutDebugLevels
	{
		None,
		TightParents,
		DirectParents,
		All,
	}
	public static LayoutDebugLevels LayoutDebugLevel { get; private set; }

	public static bool RegenerateDisabled { get; private set; }


#if DEBUG
	private static ConsoleControls m_controls;

	private static int m_controlsVisualizationIdx = -1;
	private static GameObject m_controlsVisualization;


	private void Awake()
	{
		m_controls = new();

#if UNITY_EDITOR
		// TODO: detect even while editor is paused?
		m_controls.Console.Pause.performed += ctx => UnityEditor.EditorApplication.isPaused = !UnityEditor.EditorApplication.isPaused; // NOTE that KeyCode.Pause is the 'Pause/Break' keyboard key, not Esc
#endif
		m_controls.Console.TimerPause.performed += ctx => ExecuteIfConsoleOpen(() => TimerPaused = !TimerPaused);
		m_controls.Console.NeverDie.performed += ctx => ExecuteIfConsoleOpen(() => NeverDie = !NeverDie);
		m_controls.Console.PassiveAI.performed += ctx => ExecuteIfConsoleOpen(() => PassiveAI = !PassiveAI);
		m_controls.Console.AIDebugLevel.performed += ctx => ExecuteIfConsoleOpen(() => AIDebugLevel = (AIDebugLevel + 1) % Utility.EnumNumTypes<AIDebugLevels>());
		m_controls.Console.ItemDebugLevel.performed += ctx => ExecuteIfConsoleOpen(() => ItemDebugLevel = (ItemDebugLevel - 1).Modulo(Utility.EnumNumTypes<ItemDebugLevels>()));
		m_controls.Console.ExplosionDebug.performed += ctx => ExecuteIfConsoleOpen(() => ExplosionDebug = !ExplosionDebug);
		m_controls.Console.LayoutDebugLevel.performed += ctx => ExecuteIfConsoleOpen(() => LayoutDebugLevel = (LayoutDebugLevels)(((int)LayoutDebugLevel + 1) % Utility.EnumNumTypes<LayoutDebugLevels>()));
		m_controls.Console.LayoutOutputSeed.performed += ctx => ExecuteIfConsoleOpen(() => Debug.Log(GameController.Seed));
		m_controls.Console.RegenerateDisabled.performed += ctx => ExecuteIfConsoleOpen(() => RegenerateDisabled = !RegenerateDisabled);
		m_controls.Console.SpawnEnemyWave.performed += ctx => ExecuteIfConsoleOpen(() => GameController.Instance.SpawnEnemyWave());
		m_controls.Console.KillAllEnemies.performed += ctx => ExecuteIfConsoleOpen(() => GameController.Instance.DebugKillAllEnemies());
		m_controls.Console.ReSetWaves.performed += ctx => ExecuteIfConsoleOpen(() => GameController.Instance.DebugResetWaves());
		m_controls.Console.HarmHealAvatar.performed += ctx => ExecuteIfConsoleOpen(() =>
		{
			Health avatarHealth = GameController.Instance.m_avatars.First().GetComponent<Health>();
			if (m_controls.Console.Shift.IsPressed())
			{
				avatarHealth.Increment();
			}
			else
			{
				avatarHealth.Decrement(gameObject);
			}
		});
		m_controls.Console.SpawnEnemy.performed += ctx => ExecuteIfConsoleOpen(() => GameController.Instance.DebugSpawnEnemy(Mathf.RoundToInt(m_controls.Console.SpawnEnemy.ReadValue<float>()) % 10));
		m_controls.Console.EnemiesPathfindTest.performed += ctx => ExecuteIfConsoleOpen(() =>
		{
			foreach (AIController ai in FindObjectsOfType<AIController>())
			{
				ai.DebugPathfindTest();
			}
		});
		m_controls.Console.ControlsVisualization.performed += ctx => ExecuteIfConsoleOpen(() =>
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
		});
		m_controls.Console.UnlockAll.performed += ctx =>
		{
			if (m_controls.Console.Shift.IsPressed())
			{
				GameController.DebugToggleAllZones();
			}
			else
			{
				foreach (LockController lockObj in FindObjectsOfType<LockController>())
				{
					lockObj.Unlock(null);
				}
			}
		};
		m_controls.Console.InteractDialogueRandomize.performed += ctx =>
		{
			GameController.Instance.NpcsRandomize();
			foreach (InteractDialogue interact in FindObjectsOfType<InteractDialogue>())
			{
				interact.DebugReset();
			}
		};
		m_controls.Console.MusicTest.performed += ctx => ExecuteIfConsoleOpen(() => GameController.Instance.GetComponent<MusicManager>().DebugTest());
	}

	private void OnDisable()
	{
		m_controls.Disable();
	}

	private void OnEnable()
	{
		m_controls.Enable();
	}


	private static void ExecuteIfConsoleOpen(System.Action action)
	{
		if (m_controls.Console.Toggle.IsPressed())
		{
			action();
		}
	}
#endif // DEBUG
}
