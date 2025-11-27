using System.Runtime.InteropServices;

namespace Hartonomous.CodeGeneration.TreeSitter.Native;

/// <summary>
/// P/Invoke declarations for the native TreeSitter library (tree-sitter.dll/libtree-sitter.so)
/// Based on tree_sitter/api.h from https://github.com/tree-sitter/tree-sitter
/// </summary>
internal static unsafe class TreeSitterNative
{
    private const string LibraryName = "tree-sitter";

    #region Language

    /// <summary>
    /// An opaque object that represents a language grammar
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TSLanguage { }

    /// <summary>
    /// Get the ABI version of the language
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ts_language_version(TSLanguage* language);

    /// <summary>
    /// Get the number of distinct node types in the language
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ts_language_symbol_count(TSLanguage* language);

    /// <summary>
    /// Get the name of a node type
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* ts_language_symbol_name(TSLanguage* language, ushort symbol);

    #endregion

    #region Parser

    /// <summary>
    /// An opaque object that parses source code
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TSParser { }

    /// <summary>
    /// Create a new parser
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSParser* ts_parser_new();

    /// <summary>
    /// Delete the parser, freeing all memory
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ts_parser_delete(TSParser* parser);

    /// <summary>
    /// Set the language that the parser should use for parsing
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_parser_set_language(TSParser* parser, TSLanguage* language);

    /// <summary>
    /// Parse a string of source code
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSTree* ts_parser_parse_string(
        TSParser* parser,
        TSTree* old_tree,
        byte* source,
        uint length);

    #endregion

    #region Tree

    /// <summary>
    /// An opaque object that represents a syntax tree
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TSTree { }

    /// <summary>
    /// Delete the syntax tree, freeing all memory
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ts_tree_delete(TSTree* tree);

    /// <summary>
    /// Get the root node of the syntax tree
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSNode ts_tree_root_node(TSTree* tree);

    /// <summary>
    /// Get the language that the tree was parsed with
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSLanguage* ts_tree_language(TSTree* tree);

    #endregion

    #region Node

    /// <summary>
    /// A single node in a syntax tree (passed by value, contains 32 bytes)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TSNode
    {
        internal fixed uint context[4];
        internal void* id;
        internal TSTree* tree;
    }

    /// <summary>
    /// Check if the node is null (represents no node)
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_node_is_null(TSNode node);

    /// <summary>
    /// Get the node's type as a string
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* ts_node_type(TSNode node);

    /// <summary>
    /// Get the node's type as a numerical symbol
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ushort ts_node_symbol(TSNode node);

    /// <summary>
    /// Get the node's start byte
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ts_node_start_byte(TSNode node);

    /// <summary>
    /// Get the node's end byte
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ts_node_end_byte(TSNode node);

    /// <summary>
    /// Get the node's start position (line/column)
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSPoint ts_node_start_point(TSNode node);

    /// <summary>
    /// Get the node's end position (line/column)
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSPoint ts_node_end_point(TSNode node);

    /// <summary>
    /// Get the node's number of children
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint ts_node_child_count(TSNode node);

    /// <summary>
    /// Get a child node by index
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSNode ts_node_child(TSNode node, uint index);

    /// <summary>
    /// Get the node's parent
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSNode ts_node_parent(TSNode node);

    /// <summary>
    /// Check if the node is named (vs anonymous like punctuation)
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_node_is_named(TSNode node);

    /// <summary>
    /// Check if the node represents a syntax error
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_node_is_error(TSNode node);

    /// <summary>
    /// Check if the node is a syntax error or contains errors
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_node_has_error(TSNode node);

    /// <summary>
    /// Get the node's text as a substring of the source
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern byte* ts_node_string(TSNode node);

    #endregion

    #region Point

    /// <summary>
    /// A position in a source file (row/column)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TSPoint
    {
        internal uint row;
        internal uint column;
    }

    #endregion

    #region Tree Cursor (for efficient traversal)

    /// <summary>
    /// A stateful object for walking a syntax tree efficiently
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct TSTreeCursor
    {
        internal TSTree* tree;
        internal void* id;
        internal fixed uint context[3];
    }

    /// <summary>
    /// Create a new tree cursor starting at the given node
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSTreeCursor ts_tree_cursor_new(TSNode node);

    /// <summary>
    /// Delete the tree cursor, freeing memory
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ts_tree_cursor_delete(TSTreeCursor* cursor);

    /// <summary>
    /// Get the current node of the cursor
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern TSNode ts_tree_cursor_current_node(TSTreeCursor* cursor);

    /// <summary>
    /// Move the cursor to the first child
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_tree_cursor_goto_first_child(TSTreeCursor* cursor);

    /// <summary>
    /// Move the cursor to the next sibling
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_tree_cursor_goto_next_sibling(TSTreeCursor* cursor);

    /// <summary>
    /// Move the cursor to the parent
    /// </summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool ts_tree_cursor_goto_parent(TSTreeCursor* cursor);

    #endregion
}
