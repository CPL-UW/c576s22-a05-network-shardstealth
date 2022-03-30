// using Random = UnityEngine.Random;

using System;
using System.Diagnostics.CodeAnalysis;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;
using static Netris;

// ReSharper disable once InconsistentNaming
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class GMScript : NetworkBehaviour
{
    public TileBase pieceTile;
    public TileBase emptyTile;
    public TileBase chunkTile;
    public Tilemap boardMap;
    public Tilemap enemyMap;
    public TMP_Text infoText;
    public TMP_Text dashText;
    public bool DEBUG_MODE;
    
    private int _difficulty;
    private int _fixedUpdateCount;
    private int _fixedUpdateFramesToWait = 10;
    private int _inARow;
    private bool _initialized;
    private int _score;
    
    private RectInt _hBounds = new(BOUNDS_MAX, BOUNDS_MAX, -BOUNDS_MAX, -BOUNDS_MAX);
    private RectInt _eBounds = new(BOUNDS_MAX, BOUNDS_MAX, -BOUNDS_MAX, -BOUNDS_MAX);
    
    private Vector3Int[] _myChunk;
    private Vector3Int[] _myPiece;
    private Vector3Int[] _enemyChunk;
    private Vector3Int[] _enemyPiece;

    private bool _networkStarted;
    private bool _networkRegistered;

    public GMScript()
    {
        _myPiece = null;
        _enemyPiece = null;
    }

    private bool Dirty { get; set; }
    
    private void Start()
    {
        Dirty = true;
        _initialized = false;
    }

    private void CheckValidGame()
    {
        if (null != _myPiece) return;
        _myPiece = CreateAPiece(_hBounds);
        if (!ValidState(_hBounds, _myPiece, _myChunk))
        {
            Debug.Log("NO VALID MOVE");
            Debug.Break();
        }

    }

    private const int MAX_MESSAGE = 1024;

    static void SendMessageToAll(string message_type, string message) {
        if (NetworkManager.Singleton.LocalClientId != NetworkManager.Singleton.ServerClientId)
        {
            using var a_writer = new FastBufferWriter(MAX_MESSAGE, Allocator.Temp);
            {
                a_writer.WriteValueSafe(message);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(message_type,
                    NetworkManager.Singleton.ServerClientId, a_writer);

            }
        } else {
            using var b_writer = new FastBufferWriter(MAX_MESSAGE, Allocator.Temp);
            {
                b_writer.WriteValueSafe(message);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(message_type, b_writer);
            }
        }
    }

    private const string MSG_TYPE_CHUNK = "CHUNK";
    private const string MSG_TYPE_PIECE = "PIECE";

    private void SendChunkMessage()
    {
        SendMessageToAll(MSG_TYPE_CHUNK,v2s(_myChunk));
    }

    private void SendPieceMessage()
    {
        SendMessageToAll(MSG_TYPE_PIECE,v2s(_myPiece));
    }

    void DoNetworkUpdate()
    {
        if (!_networkStarted) return;
        if(!_networkRegistered)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(MSG_TYPE_CHUNK, ReceiveChunkMessage);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(MSG_TYPE_PIECE, ReceivePieceMessage);
            _networkRegistered = true;
            return;
        }
        SendChunkMessage();
        SendPieceMessage();
    }

    private void ReceivePieceMessage(ulong senderID, FastBufferReader reader)
    {
        Dirty = true;
        reader.ReadValueSafe(out var message);
        _enemyPiece = SwitchBounds(s2v(message),_hBounds,_eBounds);
    }

    private void ReceiveChunkMessage(ulong senderID, FastBufferReader reader)
    {
        Dirty = true;
        reader.ReadValueSafe(out var message);
        _enemyChunk = SwitchBounds(s2v(message),_hBounds,_eBounds);
    }
    
    
    
    private void Update()
    {
        if (null == Camera.main) return;
        if (!_initialized)
        {
            //Debug.Log(DoTests() ? "TESTS PASSED" : "TESTS FAILED");
            SetupBaseBoards();
        }

        
        CheckValidGame();
        
        if (Input.GetKeyDown(KeyCode.Q))
            Debug.Break();
        else if (Input.GetKeyDown(KeyCode.LeftArrow))
            PlayerMove(-1,0);
        else if (Input.GetKeyDown(KeyCode.RightArrow))
            PlayerMove(1,0);
        else if (Input.GetKeyDown(KeyCode.UpArrow))
            PlayerRotate();
        else if (Input.GetKeyDown(KeyCode.DownArrow))
            PlayerDrop();


        if (!Dirty) return;
        DrawAllBoards();
        DrawAllPieces();
        Dirty = false;
    }


    private void FixedUpdate()
    {
        try
        {
            DoNetworkUpdate();
            infoText.text = "Network OK";
        }
        catch (Exception s)
        {
            infoText.text = s.Message;
        }

        if (0 != _fixedUpdateCount++ % _fixedUpdateFramesToWait) return;
        
        PlayerMove(0,-1); // tick down
        _myChunk = UpdateKillBoard(_hBounds, _myChunk);
        
        // infoText.text = $"PTS:{_score}\t\tMAX:{_difficulty}\nCURRIC 576";
        _fixedUpdateCount = 1;
    }



    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 300));
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            StartButtons();
        else
            StatusLabels();

        GUILayout.EndArea();
    }


    private void BlankABoard(Tilemap map, int x1, int y1, int x2, int y2)
    {
        for (var j = y1; j <= y2; j++)
        for (var i = x1; i <= x2; i++)
            map.SetTile(new Vector3Int(i, j, 0), emptyTile);
    }

    private void BlankAllBoards()
    {
        BlankABoard(boardMap, _hBounds.x, _hBounds.y, _hBounds.xMax, _hBounds.yMax);
        BlankABoard(enemyMap, _eBounds.x, _eBounds.y, _eBounds.xMax, _eBounds.yMax);
    }

    private void SetupBaseBoards()
    {
        _initialized = true;
        for (var wy = -1 * BOUNDS_MAX; wy < BOUNDS_MAX; wy++)
        for (var wx = -1 * BOUNDS_MAX; wx < BOUNDS_MAX; wx++)
        {
            var myTile = boardMap.GetTile(new Vector3Int(wx, wy, 0));
            var enemyTile = enemyMap.GetTile(new Vector3Int(wx, wy, 0));
            if (myTile)
            {
                if (wx < _hBounds.x) _hBounds.x = wx;
                if (wy < _hBounds.y) _hBounds.y = wy;
                if (wx > _hBounds.xMax) _hBounds.xMax = wx;
                if (wy > _hBounds.yMax) _hBounds.yMax = wy;
            }

            if (enemyTile)
            {
                if (wx < _eBounds.x) _eBounds.x = wx;
                if (wy < _eBounds.y) _eBounds.y = wy;
                if (wx > _eBounds.xMax) _eBounds.xMax = wx;
                if (wy > _eBounds.yMax) _eBounds.yMax = wy;
            }
        }

        BlankAllBoards();
        _myPiece = CreateAPiece(_hBounds);

        Debug.Log(
            $"MY BOARD SIZE = {1 + _hBounds.xMax - _hBounds.x} x {1 + _hBounds.yMax - _hBounds.y} ({_hBounds.x},{_hBounds.y}) -> ({_hBounds.xMax},{_hBounds.yMax})");
        Debug.Log(
            $"AI BOARD SIZE = {1 + _eBounds.xMax - _eBounds.x} x {1 + _eBounds.yMax - _eBounds.y} ({_eBounds.x},{_eBounds.y}) -> ({_eBounds.xMax},{_eBounds.yMax})");
    }


    private void PlayerDrop()
    {
        Dirty = true;
        (_myPiece, _myChunk) = DropPiece(_hBounds,_myPiece,_myChunk);
    }

    private void PlayerMove(int dx,int dy)
    {
        Dirty = true;
        (_myPiece, _myChunk) = ShiftPiece(dx,dy,_hBounds,_myPiece,_myChunk);
    }

    private void PlayerRotate()
    {
        Dirty = true;
        _myPiece = RotatePiece(_hBounds,_myPiece,_myChunk);
    }
    
    private void DrawAllBoards()
    {
        BlankAllBoards();

        if (null != _myChunk)
            foreach (var p in _myChunk)
                boardMap.SetTile(p, chunkTile);

        if (null != _enemyChunk)
            foreach (var p in _enemyChunk)
                enemyMap.SetTile(p, chunkTile);
    }

    private void DrawAllPieces()
    {
        if (null != _myPiece)
            foreach (var p in _myPiece)
                boardMap.SetTile(p, pieceTile);

        if (null != _enemyPiece)
            foreach (var p in _enemyPiece)
                enemyMap.SetTile(p, pieceTile);
    }

    private void StartButtons()
    {
        if (GUILayout.Button("Host")) { 
            NetworkManager.Singleton.StartHost();
            _networkStarted = true;
        }
        if (GUILayout.Button("Client"))
        {
            
            NetworkManager.Singleton.StartClient();
            _networkStarted = true;
        }
        if (GUILayout.Button("Server"))
        {
            NetworkManager.Singleton.StartServer();
            _networkStarted = true;
        }

    }

    private static void StatusLabels()
    {
        var mode = NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client";

        GUILayout.Label("Transport: " +
                        NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetType().Name);
        GUILayout.Label("Mode: " + mode);
    }
}