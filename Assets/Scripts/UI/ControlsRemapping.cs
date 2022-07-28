using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.InputSystem;


// edited from https://dastmo.com/tutorials/unity-input-system-persistent-rebinds/
public class ControlsRemapping : MonoBehaviour
{
	[SerializeField] private InputActionAsset m_controls;


	public static InputActionAsset Controls;
	public static Action<InputAction> SuccessfulRebinding;


	private const string m_filename = "/controlsOverrides.dat";
	private static string Filepath => Application.persistentDataPath + m_filename;


	private void Awake()
	{
		if (Controls != null)
		{
			Destroy(this);
			return;
		}

		Controls = m_controls;

		if (File.Exists(Filepath))
		{
			LoadControlOverrides();
		}
	}


	public static void RemapKeyboardAction(InputAction actionToRebind, int targetBinding)
	{
		actionToRebind.PerformInteractiveRebinding(targetBinding)
			.WithControlsHavingToMatchPath("<Keyboard>")
			.WithBindingGroup("Keyboard")
			.WithCancelingThrough("<Keyboard>/escape")
			.OnCancel(operation => SuccessfulRebinding?.Invoke(null))
			.OnComplete(operation => {
				operation.Dispose();
				SaveControlOverrides();
				SuccessfulRebinding?.Invoke(actionToRebind);
			})
			.Start();
	}

	// TODO: remove/combine?
	public static void RemapGamepadAction(InputAction actionToRebind, int targetBinding)
	{
		actionToRebind.PerformInteractiveRebinding(targetBinding)
			.WithControlsHavingToMatchPath("<Gamepad>")
			.WithBindingGroup("Gamepad")
			.WithCancelingThrough("<Keyboard>/escape")
			.OnCancel(operation => SuccessfulRebinding?.Invoke(null))
			.OnComplete(operation => {
				operation.Dispose();
				SaveControlOverrides();
				SuccessfulRebinding?.Invoke(actionToRebind);
			})
			.Start();
	}

	public static void RestoreDefaults()
	{
		Controls.RemoveAllBindingOverrides();
		File.Delete(Filepath);
		foreach (InputActionDisplay display in FindObjectsOfType<InputActionDisplay>()) // TODO: efficiency?
		{
			display.RefreshButtonText();
		}
	}


	private static void SaveControlOverrides()
	{
		using FileStream file = new(Filepath, FileMode.OpenOrCreate);
		new BinaryFormatter().Serialize(file, Controls.SaveBindingOverridesAsJson());
	}

	private static void LoadControlOverrides()
	{
		if (!File.Exists(Filepath))
		{
			return;
		}

		using FileStream file = new(Filepath, FileMode.Open);
		Controls.LoadBindingOverridesFromJson(new BinaryFormatter().Deserialize(file) as string);
	}
}
