// Copyright (c) 2014 Cranium Software

// SmartConsole
//
// A Quake style debug console where you can add variables with the
// CreateVariable functions and commands with the RegisterCommand functions
// the variables wrap their underlying types, the commands are delegates
// taking a string with the line of console input and returning void

// TODO:
// * sort out spammy history and 'return' key handling on mobile platforms
// * improve cvar interface
// * allow history to scroll
// * improve autocomplete
// * allow executing console script from file

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

// SE: broadly patterned after the debug console implementation from GLToy...
// https://code.google.com/p/gltoy/source/browse/trunk/GLToy/Independent/Core/Console/GLToy_Console.h

/// <summary>
/// A Quake style debug console - should be added to an otherwise empty game object and have a font set in the inspector
/// </summary>
public class SmartConsole : MonoBehaviour
{
	public delegate void ConsoleCommandFunction( string parameters );

	// allow to specify font (because we need one imported)
    /// <summary>
	/// The font used to render the console
	/// </summary>
	public Font m_font = null;
	
	// control the general layout here
	
	private const float k_animTime = 0.4f;
	private const float k_lineSpace = 0.05f;
	private const int k_historyLines = 120;
	private static Vector3 k_position = new Vector3( 0.01f, 0.65f, 0.0f );
	private static Vector3 k_fullPosition = new Vector3( 0.01f, 0.05f, 0.0f );
	private static Vector3 k_hidePosition = new Vector3( 0.01f, 1.1f, 0.0f );
	private static Vector3 k_scale = new Vector3( 0.5f, 0.5f, 1.0f );

	// SE: annoying having to leak this out publicly - basically to facilitate the weird and wonderful cvar implementation
	/// <summary>
	/// A class representing a console command - WARNING: this is only exposed as a hack!
	/// </summary>
	public class Command
	{
		public ConsoleCommandFunction m_callback = null;
		public string m_name = null;
		public string m_paramsExample = "";
		public string m_help = "(no description)";
	};
	
	// SE - this is a bit elaborate, needed to provide a way to do this
	// without relying on memory addresses or pointers... which has resulted in
	// a little blob of bloat and overhead for something that should be trivial... :/

	/// <summary>
	/// A class representing a console variable
	/// </summary>
	public class Variable< T > : Command where T : new()
	{
		public Variable( string name )
		{
			Initialise( name, "", new T() );
		}
		
		public Variable( string name, string description )
		{
			Initialise( name, description, new T() );
		}
		
		public Variable( string name, T initialValue )
		{
			Initialise( name, "", initialValue );
		}
		
		public Variable( string name, string description, T initalValue )
		{
			Initialise( name, description, initalValue );
		}

		public void Set( T val ) // SE: I don't seem to know enough C# to provide a user friendly assignment operator solution
		{
			m_value = val;
		}
		
		public static implicit operator T( Variable< T > var )
		{
			return var.m_value;
		}

		private void Initialise( string name, string description, T initalValue )
		{
			m_name = name;
			m_help = description;
			m_paramsExample = "";
			m_value = initalValue;
			m_callback = CommandFunction;
		}

		private static void CommandFunction( string parameters )
		{
			string[] split = CVarParameterSplit( parameters );
			if( ( split.Length != 0 ) && s_variableDictionary.ContainsKey( split[ 0 ] ) )
			{
				Variable< T > variable = s_variableDictionary[ split[ 0 ] ] as Variable< T >;
				string conjunction = " is set to ";
				if( split.Length == 2 )
				{
					variable.SetFromString( split[ 1 ] );
					conjunction = " has been set to ";
				}

				WriteLine( variable.m_name + conjunction + variable.m_value );
			}
		}

		private void SetFromString( string value )
		{
			m_value = (T)System.Convert.ChangeType( value, typeof( T ) );
		}
		
		private T m_value;
	};

	// ...

	void Awake()
	{
		if( !gameObject.activeSelf )
		{
			return;
		}

		if( m_font == null )
		{
			Debug.LogError( "SmartConsole requires a font to be set in the inspector" );
		}

		Initialise( this );
	}

	// SE: these can be made non-static to enable some functionality when paused and un-paused
	// however... there are a lot of problems when making changes when paused anyway...
	// so instead I've left them static to detect that case and warn the user that they are doing
	// something flakey instead...
	static int s_flippy = 0;
	static bool s_blink = false;
	static bool s_first = true;
	void Update()
	{
		if( !gameObject.activeSelf )
		{
			return;
		}

		// SE - delayed initialisation. unfortunate
		if( s_first )
		{
			if( ( s_fps == null ) || ( s_textInput == null ) )
			{
				Debug.LogWarning( "Some variables are null that really shouldn't be! Did you make code changes whilst paused? Be aware that such changes are not safe in general!" );
				return; // SE: can't do anything meaningful here, probably the user recompiled things
			}

			SetTopDrawOrderOnGUIText( s_fps.guiText );
			SetTopDrawOrderOnGUIText( s_textInput.guiText );
			foreach( GameObject historyLine in s_historyDisplay )
			{
				SetTopDrawOrderOnGUIText( historyLine.guiText );
			}

			s_first = false;
		}

		HandleInput();

		if( s_showConsole )
		{
			s_visiblityLerp += Time.deltaTime / k_animTime;
		}
		else
		{
			s_visiblityLerp -= Time.deltaTime / k_animTime;
		}

		s_visiblityLerp = Mathf.Clamp01( s_visiblityLerp );

		transform.position = Vector3.Lerp( k_hidePosition, ( s_drawFullConsole ? k_fullPosition : k_position ), SmootherStep( s_visiblityLerp ) );
		transform.localScale = k_scale;

		if( ( s_textInput != null )
		   && ( s_textInput.guiText != null ) )
		{
			s_textInput.guiText.text = ">" + s_currentInputLine + ( ( s_blink ) ? "_" : "" );
		}

		++s_flippy;
		s_flippy &= 7;
		if( s_flippy == 0 )
		{
			s_blink = !s_blink;
		}

		if( s_drawFPS )
		{
			s_fps.guiText.text = "" + ( 1.0f / Time.deltaTime ) + " fps ";
			s_fps.transform.position = new Vector3( 0.8f, 1.0f, 0.0f );
		}
		else
		{
			s_fps.transform.position = new Vector3( 1.0f, 10.0f, 0.0f );
		}
	}

	/// <summary>
	/// Clears out the console log
	/// </summary>
	/// <example> 
	/// <code>
	/// SmartConsole.Clear();
	/// </code>
	/// </example>
	public static void Clear()
	{
		s_outputHistory.Clear();
		SetStringsOnHistoryElements();
	}

	/// <summary>
	/// Write a message to the debug console (only - not the log)
	/// </summary>
	/// <param name="message">
	/// The message to display
	/// </param>
	/// <example>
	/// <code>
	/// SmartConsole.Print( "Hello world!" );
	/// </code>
	/// </example>
	public static void Print( string message )
	{
		WriteLine( message );
	}

	/// <summary>
	/// Write a message to the debug console (only - not the log)
	/// </summary>
	/// <param name="message">
	/// The message to display
	/// </param>
	/// <example>
	/// <code>
	/// SmartConsole.WriteLine( "Hello world!" );
	/// </code>
	/// </example>
	public static void WriteLine( string message )
	{
		s_outputHistory.Add( DeNewLine( message ) );
		s_currentCommandHistoryIndex = s_outputHistory.Count - 1;
		SetStringsOnHistoryElements();
	}

	/// <summary>
	/// Execute a string as if it were a single line of input to the console
	/// </summary>
	public static void ExecuteLine( string inputLine )
	{
		WriteLine( ">" + inputLine );
		string[] words = CComParameterSplit( inputLine );
		if( words.Length > 0 )
		{
			if( s_masterDictionary.ContainsKey( words[ 0 ] ) )
			{
				s_commandHistory.Add( inputLine );
				s_masterDictionary[ words[ 0 ] ].m_callback( inputLine );
			}
			else
			{
				WriteLine( "Unrecognised command or variable name: " + words[ 0 ] );
			}
		}
	}

	// public static void ExecuteFile( string path ) {} //...

	public static void RemoveCommandIfExists( string name )
	{
		s_commandDictionary.Remove( name );
		s_masterDictionary.Remove( name );
	}

	/// <summary>
	/// Register a console command with an example of usage and a help description
	/// e.g. SmartConsole.RegisterCommand( "echo", "echo <string>", "writes <string> to the console log", SmartConsole.Echo );
	/// </summary>
	public static void RegisterCommand( string name, string exampleUsage, string helpDescription, ConsoleCommandFunction callback )
	{
		Command command = new Command();
		command.m_name = name;
		command.m_paramsExample = exampleUsage;
		command.m_help = helpDescription;
		command.m_callback = callback;

		s_commandDictionary.Add( name, command );
		s_masterDictionary.Add( name, command );
	}

	/// <summary>
	/// Register a console command with a help description
	/// e.g. SmartConsole.RegisterCommand( "help", "displays help information for console command where available", SmartConsole.Help );
	/// </summary>
	public static void RegisterCommand( string name, string helpDescription, ConsoleCommandFunction callback )
	{
		RegisterCommand( name, "",  helpDescription, callback );
	}

	/// <summary>
	/// Register a console command
	/// e.g. SmartConsole.RegisterCommand( "foo", Foo );
	/// </summary>
	public static void RegisterCommand( string name, ConsoleCommandFunction callback )
	{
		RegisterCommand( name, "", "(no description)", callback );
	}

	/// <summary>
	/// Create a console variable
	/// e.g. SmartConsole.Variable< bool > showFPS = SmartConsole.CreateVariable< bool >( "show.fps", "whether to draw framerate counter or not", false );
	/// </summary>
	public static Variable< T > CreateVariable< T >( string name, string description, T initialValue ) where T : new()
	{
		if( s_variableDictionary.ContainsKey( name ) )
		{
			Debug.LogError( "Tried to add already existing console variable!" );
			return null;
		}

		Variable< T > returnValue = new Variable< T >( name, description, initialValue );
		s_variableDictionary.Add( name, returnValue );
		s_masterDictionary.Add( name, returnValue );

		return returnValue;
	}

	/// <summary>
	/// Create a console variable without specifying a default value
	/// e.g. SmartConsole.Variable< float > gameSpeed = SmartConsole.CreateVariable< float >( "game.speed", "the current speed of the game" );
	/// </summary>
	public static Variable< T > CreateVariable< T >( string name, string description ) where T : new()
	{
		return CreateVariable< T >( name, description, new T() );
	}

	/// <summary>
	/// Create a console variable without specifying a description or default value
	/// e.g. SmartConsole.Variable< string > someString = SmartConsole.CreateVariable< string >( "some.string" );
	/// </summary>
	public static Variable< T > CreateVariable< T >( string name ) where T : new()
	{
		return CreateVariable< T >( name, "" );
	}

	/// <summary>
	/// Destroy a console variable (so its name can be reused)
	/// </summary>
	public static void DestroyVariable< T >( Variable< T > variable ) where T : new()
	{
		s_variableDictionary.Remove( variable.m_name );
		s_masterDictionary.Remove( variable.m_name );
	}

	// --- commands

	private static void Help( string parameters )
	{
		// try and lay it out nicely...
		const int nameLength = 25;
		const int exampleLength = 35;
		foreach( Command command in s_commandDictionary.Values )
		{
			string outputString = command.m_name;
			for( int i = command.m_name.Length; i < nameLength; ++i )
			{
				outputString += " ";
			}

			if( command.m_paramsExample.Length > 0 )
			{
				outputString += " example: " + command.m_paramsExample;
			}
			else
			{
				outputString += "          ";
			}

			for( int i = command.m_paramsExample.Length; i < exampleLength; ++i )
			{
				outputString += " ";
			}

			WriteLine( outputString + command.m_help );
		}
	}

	private static void Echo( string parameters )
	{
		string outputMessage = "";
		string[] split = CComParameterSplit( parameters );
		for( int i = 1; i < split.Length; ++i )
		{
			outputMessage += split[ i ] + " ";
		}

		if( outputMessage.EndsWith( " " ) )
		{
			outputMessage.Substring( 0, outputMessage.Length - 1 );
		}

		WriteLine( outputMessage );
	}

	private static void Clear( string parameters )
	{
		Clear();
	}

	private static void LastExceptionCallStack( string parameters )
	{
		DumpCallStack( s_lastExceptionCallStack );
	}

	private static void LastErrorCallStack( string parameters )
	{
		DumpCallStack( s_lastErrorCallStack );
	}

	private static void LastWarningCallStack( string parameters )
	{
		DumpCallStack( s_lastWarningCallStack );
	}

	private static void Quit( string parameters )
	{
#if UNITY_EDITOR
		EditorApplication.isPlaying = false;
#else
		Application.Quit();
#endif
	}


	private static void ListCvars( string parameters )
	{
		// try and lay it out nicely...
		const int nameLength = 50;
		foreach( Command variable in s_variableDictionary.Values )
		{
			string outputString = variable.m_name;
			for( int i = variable.m_name.Length; i < nameLength; ++i )
			{
				outputString += " ";
			}
			
			WriteLine( outputString + variable.m_help );
		}
	}

	// --- internals

	private static void Initialise( SmartConsole instance )
	{
		// run this only once...
		if( s_textInput != null )
		{
			return;
		}

		Application.RegisterLogCallback( LogHandler );

		InitialiseCommands();
		InitialiseVariables();
		InitialiseUI( instance );
	}

	const float k_toogleCDTime = 0.35f;
	static float s_toggleCooldown = 0.0f;
	static int s_currentCommandHistoryIndex = 0;
	private static void HandleInput()
	{
		const float k_minFrameRate = 0.0166f;
		s_toggleCooldown += ( Time.deltaTime < k_minFrameRate ) ? k_minFrameRate : Time.deltaTime;

		if( s_toggleCooldown < k_toogleCDTime )
		{
			return;
		}

		bool tapped = false;
		if( Input.touchCount > 0 )
		{
			tapped = IsInputCoordInBounds( Input.touches[ 0 ].position );
		}
		else if( Input.GetMouseButton( 0 ) )
		{
			tapped = IsInputCoordInBounds( new Vector2( Input.mousePosition.x, Input.mousePosition.y ) );
		}
		
		if( tapped || Input.GetKeyUp( KeyCode.BackQuote ) )
		{
			if( !s_consoleLock )
			{
				s_showConsole = !s_showConsole;
				if( s_showConsole )
				{
					s_currentInputLine = ""; // clear out last input because its annoying otherwise...
	#if UNITY_IPHONE || UNITY_ANDROID
					TouchScreenKeyboard.Open( "", TouchScreenKeyboardType.Default, false, true, false, true );
	#endif
				}
			}
			s_toggleCooldown = 0.0f;
		}

		if( s_commandHistory.Count > 0 )
		{
			bool update = false;
			if( Input.GetKeyDown( KeyCode.UpArrow ) )
			{
				update = true;
				--s_currentCommandHistoryIndex;

			}
			else if( Input.GetKeyDown( KeyCode.DownArrow ) )
			{
				update = true;
				++s_currentCommandHistoryIndex;
			}

			if( update )
			{
				s_currentCommandHistoryIndex = Mathf.Clamp( s_currentCommandHistoryIndex, 0, s_commandHistory.Count - 1 );
				s_currentInputLine = s_commandHistory[ s_currentCommandHistoryIndex ];
			}
		}

		HandleTextInput();
	}

	private static void InitialiseCommands()
	{
		RegisterCommand( "clear", "clear the console log", Clear );
		RegisterCommand( "cls", "clear the console log (alias for Clear)", Clear );
		RegisterCommand( "echo", "echo <string>", "writes <string> to the console log (alias for echo)", Echo );
		RegisterCommand( "help", "displays help information for console command where available", Help );
		RegisterCommand( "list", "lists all currently registered console variables", ListCvars );
		RegisterCommand( "print", "print <string>", "writes <string> to the console log", Echo );
		RegisterCommand( "quit", "quit the game (not sure this works with iOS/Android)", Quit );
		RegisterCommand( "callstack.warning", "display the call stack for the last warning message", LastWarningCallStack );
		RegisterCommand( "callstack.error", "display the call stack for the last error message", LastErrorCallStack );
		RegisterCommand( "callstack.exception", "display the call stack for the last exception message", LastExceptionCallStack );
	}

	private static void InitialiseVariables()
	{
		s_drawFPS = CreateVariable< bool >( "show.fps", "whether to draw framerate counter or not", false );

		s_drawFullConsole = CreateVariable< bool >( "console.fullscreen", "whether to draw the console over the whole screen or not", false );
		s_consoleLock = CreateVariable< bool >( "console.lock", "whether to allow showing/hiding the console", false );
		s_logging = CreateVariable< bool >( "console.log", "whether to redirect log to the console", true );
	}

	private static Font s_font = null;
	private static void InitialiseUI( SmartConsole instance )
	{
		s_font = instance.m_font;
		if( s_font == null )
		{
			Debug.LogError( "SmartConsole needs to have a font set on an instance in the editor!" );
			s_font = new Font( "Arial" );
		}

		s_fps = instance.AddChildWithGUIText( "FPSCounter" );
		s_textInput = instance.AddChildWithGUIText( "SmartConsoleInputField" );
		s_historyDisplay = new GameObject[ k_historyLines ];
		for( int i = 0; i < k_historyLines; ++i )
		{
			s_historyDisplay[ i ] = instance.AddChildWithGUIText( "SmartConsoleHistoryDisplay" + i );
		}

		instance.Layout();
	}

	private GameObject AddChildWithGUIText( string name )
	{
		return AddChildWithComponent< GUIText >( name );		
	}

	private GameObject AddChildWithComponent< T >( string name ) where T : Component
	{
		GameObject returnObject = new GameObject();
		returnObject.AddComponent< T >();
		returnObject.transform.parent = transform;
		returnObject.name = name;
		return returnObject;
	}

	private static void SetTopDrawOrderOnGUIText( GUIText text )
	{
		// SE - TODO: work out how to do this. its currently pretty annoying because the text goes behind
		// why would i ever want that behaviour?!?!?!
		//MaterialManager.SetRenderLayer( text.renderer, RenderLayers.OVERLAY, false );
		//text.renderer.sortingOrder = 0;
	}

	private static void HandleTextInput()
	{
		bool autoCompleteHandled = false;
		foreach( char c in Input.inputString )
		{
			switch( c )
			{
				case '\b': 	s_currentInputLine = ( s_currentInputLine.Length > 0 ) ? s_currentInputLine.Substring( 0, s_currentInputLine.Length - 1 ) : ""; break;
				case '\n':
				case '\r': 	ExecuteCurrentLine(); s_currentInputLine = ""; break;
				case '\t': 	AutoComplete(); autoCompleteHandled = true; break; // SE - unity doesn't seem to give this here so we check a keydown as well...
				default: 	s_currentInputLine = s_currentInputLine + c; break;
			}
		}

		if( !autoCompleteHandled && Input.GetKeyDown( KeyCode.Tab ) )
		{
			AutoComplete();
		}
	}
	
	private static void ExecuteCurrentLine()
	{
		ExecuteLine( s_currentInputLine );
	}

	private static void AutoComplete()
	{
		string[] lookup = CComParameterSplit( s_currentInputLine );
		if( lookup.Length == 0 )
		{
			// don't auto complete if we have typed any parameters so far or nothing at all...
			return;
		}

		Command nearestMatch = s_masterDictionary.AutoCompleteLookup( lookup[ 0 ] );

		// only complete to the next dot if there is one present in the completion string which
		// we don't already have in the lookup string
		int dotIndex = 0;
		do
		{
			dotIndex = nearestMatch.m_name.IndexOf( ".", dotIndex + 1 );
		}
		while( ( dotIndex > 0 ) && ( dotIndex < lookup[ 0 ].Length ) );

		string insertion = nearestMatch.m_name;
		if( dotIndex >= 0 )
		{
			insertion = nearestMatch.m_name.Substring( 0, dotIndex + 1 );
		}

		if( insertion.Length < s_currentInputLine.Length )
		{
			do
			{
				if( AutoCompleteTailString( "true" ) ) break;
				if( AutoCompleteTailString( "false" ) ) break;
				if( AutoCompleteTailString( "True" ) ) break;
				if( AutoCompleteTailString( "False" ) ) break;
				if( AutoCompleteTailString( "TRUE" ) ) break;
				if( AutoCompleteTailString( "FALSE" ) ) break;
			}
			while( false );
		}
		else if( insertion.Length >= s_currentInputLine.Length ) // SE - is this really correct?
		{
			s_currentInputLine = insertion;
		}
	}

	private static bool AutoCompleteTailString( string tailString )
	{
		for( int i = 1; i < tailString.Length; ++i )
		{
			if( s_currentInputLine.EndsWith( " " + tailString.Substring( 0, i ) ) )
			{
				s_currentInputLine = s_currentInputLine.Substring( 0, s_currentInputLine.Length - 1 ) + tailString.Substring( i - 1 );
				return true;
			}
		}

		return false;
	}

	private void Layout()
	{
		float y = 0.0f;
		LayoutTextAtY( s_textInput, y );
		LayoutTextAtY( s_fps, y );
		y += k_lineSpace;
		for( int i = 0; i < k_historyLines; ++i )
		{
			LayoutTextAtY( s_historyDisplay[ i ], y );
			y += k_lineSpace;
		}
	}

	private static void LayoutTextAtY( GameObject o, float y )
	{
		o.transform.localPosition = new Vector3( 0.0f, y, 0.0f );
		o.guiText.fontStyle = FontStyle.Normal;
		o.guiText.font = s_font;
	}

	private static void SetStringsOnHistoryElements()
	{
		for( int i = 0; i < k_historyLines; ++i )
		{
			int historyIndex = s_outputHistory.Count - 1 - i;
			if( historyIndex >= 0 )
			{
				s_historyDisplay[ i ].guiText.text = s_outputHistory[ s_outputHistory.Count - 1 - i ];
			}
			else
			{
				s_historyDisplay[ i ].guiText.text = "";
			}
		}
	}

	private static bool IsInputCoordInBounds( Vector2 inputCoordinate )
	{
		return ( inputCoordinate.x < ( 0.05f * Screen.width ) ) && ( inputCoordinate.y > ( 0.95f * Screen.height ) );
	}

	private static void LogHandler( string message, string stack, LogType type )
	{
		if( !s_logging )
		{
			return;
		}

		string assertPrefix		= "[Assert]:             ";
		string errorPrefix 		= "[Debug.LogError]:     ";
		string exceptPrefix 	= "[Debug.LogException]: ";
		string warningPrefix 	= "[Debug.LogWarning]:   ";
		string otherPrefix 		= "[Debug.Log]:          ";
		
		string prefix = otherPrefix;
		switch( type )
		{
			case LogType.Assert:
			{
				prefix = assertPrefix;
				break;
			}
				
			case LogType.Warning:
			{
				prefix = warningPrefix;
				s_lastWarningCallStack = stack;
				break;
			}
				
			case LogType.Error:
			{
				prefix = errorPrefix;
				s_lastErrorCallStack = stack;
				break;
			}
				
			case LogType.Exception:
			{
				prefix = exceptPrefix;
				s_lastExceptionCallStack = stack;
				break;
			}
				
			default:
			{
				break;
			}
		}
		
		WriteLine( prefix + message );
		
		switch( type )
		{
			case LogType.Assert:
			case LogType.Error:
			case LogType.Exception:
			{
				//WriteLine ( "Call stack:\n" + stack );
				break;
			}
				
			default:
			{
				break;
			}
		}
	}

	public static string[] CComParameterSplit( string parameters )
	{
		return parameters.Split( new char[]{ ' ' }, System.StringSplitOptions.RemoveEmptyEntries );
	}

	public static string[] CComParameterSplit( string parameters, int requiredParameters )
	{
		string[] split = CComParameterSplit( parameters );
		if( split.Length < ( requiredParameters + 1 ) )
		{
			WriteLine( "Error: not enough parameters for command. Expected " + requiredParameters + " found " + ( split.Length - 1 ) );
		}

		if( split.Length > ( requiredParameters + 1 ) )
		{
			int extras = ( ( split.Length - 1 ) - requiredParameters );
			WriteLine( "Warning: " + extras + "additional parameters will be dropped:" );
			for( int i = split.Length - extras; i < split.Length; ++i )
			{
				WriteLine ( "\"" + split[ i ] + "\"" );
			}
		}

		return split;
	}

	private static string[] CVarParameterSplit( string parameters )
	{
		string[] split = CComParameterSplit( parameters );
		if( split.Length == 0 )
		{
			WriteLine( "Error: not enough parameters to set or display the value of a console variable." );
		}
		
		if( split.Length > 2 )
		{
			int extras = ( split.Length - 3 );
			WriteLine( "Warning: " + extras + "additional parameters will be dropped:" );
			for( int i = split.Length - extras; i < split.Length; ++i )
			{
				WriteLine ( "\"" + split[ i ] + "\"" );
			}
		}
		
		return split;
	}

	private static string DeNewLine( string message )
	{
		return message.Replace( "\n", " | " );
	}

	private static void DumpCallStack( string stackString )
	{
		string[] lines = stackString.Split( new char[]{ '\r', '\n' } );

		if( lines.Length == 0 )
		{
			return;
		}

		int ignoreCount = 0;
		while( ( lines[ lines.Length - 1 - ignoreCount ].Length == 0 ) && ( ignoreCount < lines.Length ) )
		{
			++ignoreCount;
		}
		int lineCount = lines.Length - ignoreCount;
		for( int i = 0; i < lineCount; ++i )
		{
			// SE - if the call stack is 100 deep without recursion you have much bigger problems than you can ever solve with a debugger...
			WriteLine( ( i + 1 ).ToString() + ( ( i < 9 ) ? "  " : " " ) + lines[ i ] );
		}
	}

	private class AutoCompleteDictionary< T >: SortedDictionary< string, T >
	{
		public AutoCompleteDictionary()
		: base( new AutoCompleteComparer() )
		{
			m_comparer = this.Comparer as AutoCompleteComparer;
		}
		
		public T LowerBound( string lookupString )
		{
			m_comparer.Reset();
			this.ContainsKey( lookupString );
			return this[ m_comparer.LowerBound ];
		}
		
		public T UpperBound( string lookupString )
		{
			m_comparer.Reset();
			this.ContainsKey( lookupString );
			return this[ m_comparer.UpperBound ];
		}
		
		public T AutoCompleteLookup( string lookupString )
		{
			m_comparer.Reset();
			this.ContainsKey( lookupString );
			string key = ( m_comparer.UpperBound == null ) ? m_comparer.LowerBound : m_comparer.UpperBound;
			return this[ key ];
		}
		
		private class AutoCompleteComparer : IComparer< string >
		{
			private string m_lowerBound = null;
			private string m_upperBound = null;
			
			public string LowerBound { get{ return m_lowerBound; } }
			public string UpperBound { get{ return m_upperBound; } }
			
			public int Compare( string x, string y )
			{
				int comparison = Comparer< string >.Default.Compare( x, y );
				
				if( comparison >= 0 )
				{
					m_lowerBound = y;
				}
				
				if( comparison <= 0 )
				{
					m_upperBound = y;
				}
				
				return comparison;
			}
			
			public void Reset()
			{
				m_lowerBound = null;
				m_upperBound = null;
			}
		}
		
		private AutoCompleteComparer m_comparer;
	}

	private float SmootherStep( float t )
	{
		return ( ( 6 * t - 15 ) * t + 10 ) * t * t * t;
	}

	private static Variable< bool > s_drawFPS = null;
	private static Variable< bool > s_drawFullConsole = null;
	private static Variable< bool > s_consoleLock = null;
	private static Variable< bool > s_logging = null;

	private static GameObject s_fps = null;
	private static GameObject s_textInput = null;
	private static GameObject[] s_historyDisplay = null;

	private static AutoCompleteDictionary< Command > s_commandDictionary = new AutoCompleteDictionary< Command >();
	private static AutoCompleteDictionary< Command > s_variableDictionary = new AutoCompleteDictionary< Command >();

	private static AutoCompleteDictionary< Command > s_masterDictionary = new AutoCompleteDictionary< Command >();

	private static List< string > s_commandHistory = new List< string >();
	private static List< string > s_outputHistory = new List< string >();

	private static string s_lastExceptionCallStack = "(none yet)";
	private static string s_lastErrorCallStack = "(none yet)";
	private static string s_lastWarningCallStack = "(none yet)";

	private static string s_currentInputLine = "";

	private static float s_visiblityLerp = 0.0f;
	private static bool s_showConsole = false;
}
