// using System;
// using System.Collections.Generic;

using System;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

public static class Netris
{
    public const int BOUNDS_MAX = 25;
    private const int NO_ROW = -10 * BOUNDS_MAX;

    private static readonly Vector3Int[] PIECE_T = {new(0, -1), new(1, -1), new(0, 0), new(-1, -1)};
    private static readonly Vector3Int[] PIECE_L = {new(0, -1), new(1, -1), new(1, 0), new(-1, -1)};
    private static readonly Vector3Int[] PIECE_Z = {new(0, -1), new(1, -1), new(0, 0), new(-1, 0)};
    private static readonly Vector3Int[] PIECE_J = {new(0, -1), new(1, -1), new(-1, 0), new(-1, -1)};
    private static readonly Vector3Int[] PIECE_S = {new(0, -1), new(-1, -1), new(0, 0), new(1, 0)};
    private static readonly Vector3Int[] PIECE_I = {new(0, 0), new(-1, 0), new(-2, 0), new(1, 0)};
    private static readonly Vector3Int[][] PIECES = {PIECE_T, PIECE_L, PIECE_Z, PIECE_J, PIECE_S, PIECE_I};


    public static bool VEquals(Vector3Int[] a, Vector3Int[] b)
    {
        if (a == b) return true;
        if (a == null || b == null) return false;
        return a.All(b.Contains) && b.All(a.Contains);
    }

    public static bool DoTests()
    {
        return VEquals(PIECE_J, s2v(v2s(PIECE_J)));
    }

    public static Vector3Int[] SwitchBounds(Vector3Int[] v, RectInt originalBounds, RectInt newBounds)
    {
        var output = new Vector3Int[v.Length];
        for(var i = 0; i < v.Length; i++)
        {
            output[i].x = v[i].x - originalBounds.xMin + newBounds.xMin;
            output[i].y = v[i].y - originalBounds.yMin + newBounds.yMin;
        }
        return output;
    }
    
    public static Vector3Int[] s2v(string s)
    {

        var cs = s.Split(",");
        var output = new Vector3Int[cs.Length/2];
        for (var i = 0; i < cs.Length; i += 2)
        {
            var index = i / 2;
            if (!int.TryParse(cs[i], out _)) continue;
            output[index].x = Convert.ToInt32(cs[i]);
            output[index].y = Convert.ToInt32(cs[i + 1]);
        }
        return output;
    }

    public static string v2s(Vector3Int[] v)
    {
        return v.Aggregate("", (current, t) => current + $"{t.x},{t.y},");
    }
    
    
    private static bool ValidMove(int x, int y, RectInt bounds, Vector3Int[] chunk)
    {
        if (!ValidWorldRect(x, y, bounds))
            return false;
        return null == chunk || chunk.All(p => p.x != x || p.y != y);
    }

    public static bool ValidState(RectInt bounds, Vector3Int[] piece, Vector3Int[] chunk)
    {
        return null != piece && piece.All(p => ValidMove(p.x, p.y, bounds, chunk));
    }
    
    public static Vector3Int[] CreateAPiece(RectInt bounds)
    {
        var targetPiece = PIECES[Random.Range(0, PIECES.Length)];
        var newPiece = new Vector3Int[targetPiece.Length];
        for (var i = 0; i < targetPiece.Length; i++)
        {
            newPiece[i].x = targetPiece[i].x + (int)bounds.center.x;
            newPiece[i].y = targetPiece[i].y + bounds.yMax;
        }

        return newPiece;
    }

    public static (Vector3Int[],Vector3Int[]) ShiftPiece(int dx, int dy, RectInt bounds, Vector3Int[] piece, Vector3Int[] chunk)
    {
        if (null == piece) return (null, chunk);
        if (piece.Any(p => !ValidMove(p.x + dx, p.y + dy, bounds, chunk)))
        {
            if (dy < 0) return (CreateAPiece(bounds),ChunkPiece(piece, chunk));
            return (piece, chunk);
        }
        var newPiece = new Vector3Int[piece.Length];
        for (var i = 0; i < piece.Length; i++) newPiece[i] = new Vector3Int(piece[i].x + dx, piece[i].y + dy);
        return (newPiece, chunk);
    }
    
    
    public static Vector3Int[] RotatePiece(RectInt bounds, Vector3Int[] piece, Vector3Int[] chunk)
    {
        // rotated_x = (current_y + origin_x - origin_y)
        // rotated_y = (origin_x + origin_y - current_x - ?max_length_in_any_direction)
        if (null == piece) return null;
        var newPiece = new Vector3Int[piece.Length];
        for (var i = 0; i < piece.Length; i++) newPiece[i] = new Vector3Int(piece[i].x, piece[i].y);
        
        var origin = newPiece[0];
        for (var i = 1; i < newPiece.Length; i++)
        {
            var rotatedX = piece[i].y + origin.x - origin.y;
            var rotatedY = origin.x + origin.y - piece[i].x;
            if (!ValidMove(rotatedX, rotatedY, bounds, chunk))
                return piece;
            newPiece[i] = new Vector3Int(rotatedX, rotatedY);
        }

        return newPiece;
    }
    
    
    public static (Vector3Int[],Vector3Int[]) DropPiece(RectInt bounds, Vector3Int[] piece, Vector3Int[] chunk)
    {
        var lastPiece = piece;
        var lastChunk = chunk;
        while (chunk == lastChunk && piece != null)
        {
            piece = lastPiece;
            (lastPiece, lastChunk) = ShiftPiece( 0, -1, bounds, piece, chunk);
        }
        return (lastPiece,lastChunk);
    }


    private static Vector3Int[] ChunkPiece(Vector3Int[] piece, Vector3Int[] chunk)
    {
        chunk ??= new Vector3Int[] { };
        return null == piece ? chunk : chunk.Concat(piece).ToArray();
    }

    private static bool ValidWorldRect(int x, int y, RectInt bounds)
    {
        return x <= bounds.xMax && x >= bounds.x && y <= bounds.yMax && y >= bounds.y;
    }


    public static Vector3Int[] UpdateKillBoard(RectInt bounds, Vector3Int[] chunk)
    {
        var row_to_kill = FindKillableRow(chunk, bounds);
        return NO_ROW != row_to_kill ? KillRowNumber(chunk, row_to_kill) : chunk;
    }
    
    private static Vector3Int[] KillRowNumber(Vector3Int[] chunk, int row)
    {
        var newChunk = new Vector3Int[] { };
        foreach (var p in chunk)
            if (p.y > row)
            {
                Vector3Int[] movedPieces = {new(p.x, p.y - 1, p.z)};
                newChunk = newChunk.Concat(movedPieces).ToArray();
            }
            else if (p.y < row)
            {
                Vector3Int[] movedPieces = {p};
                newChunk = newChunk.Concat(movedPieces).ToArray();
            }

        return newChunk;
    }
    

    private static int FindKillableRow(Vector3Int[] chunk, RectInt bounds)
    {
        if (null == chunk) return NO_ROW;
        for (var row = bounds.yMin; row <= bounds.yMax; row++)
        {
            var maxCount = 1 + bounds.xMax - bounds.xMin; // width
            foreach (var p in chunk)
                if (p.y == row) maxCount--;
            if (0 == maxCount) return row;
        }
        return NO_ROW;
    }
}