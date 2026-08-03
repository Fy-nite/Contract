// Copyright (c) 2026, Compiler Explorer Authors
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
//     * Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
// POSSIBILITY OF SUCH DAMAGE.
import * as monaco from 'monaco-editor';

function definition(): monaco.languages.IMonarchLanguage {
    return {
        defaultToken: 'invalid',

        keywords: [
            'Contract',
            'Types',
            'fn',
            'fun',
            'if',
            'else',
            'while',
            'for',
            'switch',
            'case',
            'return',
            'var',
            'let',
            'static',
            'public',
            'private',
            'protected',
            'internal',
            'null',
            'import',
            'constructor',
            'struct',
            'export',
            'type',
            'new',
            'break',
            'continue',
            'true',
            'false',
        ],

        typeKeywords: [
            'int',
            'string',
            'bool',
            'double',
            'float',
            'object',
            'int64',
            'long',
            'void',
        ],

        operators: [
            '+',
            '-',
            '*',
            '/',
            '%',
            '=',
            '==',
            '!=',
            '<',
            '<=',
            '>',
            '>=',
            '&&',
            '||',
            '!',
            '->',
            '|>',
            '+=',
            '-=',
            '*=',
            '/=',
            '%=',
            '::',
            ':',
        ],

        symbols: /[=!<>&|+\-*\/%:]+/,
        escapes: /\\(?:[abfnrtv\\"']|x[0-9A-Fa-f]{2}|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})/,

        // The main tokenizer for our language
        tokenizer: {
            root: [
                // preprocessor directives (#line)
                [/^\s*#line.*$/, 'preprocessor'],

                // identifiers and keywords (keywords are case-sensitive: Contract, Types)
                [
                    /[a-z_][a-z0-9_]*/,
                    {
                        cases: {
                            '@typeKeywords': 'type',
                            '@keywords': 'keyword',
                            '@default': 'identifier',
                        },
                    },
                ],
                [
                    /[A-Z][A-Za-z0-9_]*/,
                    {
                        cases: {
                            '@keywords': 'keyword',
                            '@default': 'type.identifier',
                        },
                    },
                ],

                // whitespace and comments
                {include: '@whitespace'},

                // numbers
                [/\d+\.\d+/, 'number.float'],
                [/\d+/, 'number'],

                // strings (interpolation is handled inside the string state)
                [/"([^"\\]|\\.)*$/, 'string.invalid'], // non-terminated string
                [/"/, 'string', '@string'],

                // delimiters and operators
                [/[(){}[\]]/, '@brackets'],
                [/[.;,]/, 'delimiter'],
                [
                    /@symbols/,
                    {
                        cases: {
                            '@operators': 'operator',
                            '@default': '',
                        },
                    },
                ],
            ],

            whitespace: [
                [/[ \t\r\n]+/, 'white'],
                [/\/\/.*$/, 'comment'],
            ],

            string: [
                [/[^\\"{]+/, 'string'],
                [/@escapes/, 'string.escape'],
                [/\\./, 'string.escape.invalid'],
                [/{/, 'string', '@interpolation'],
                [/"/, 'string', '@pop'],
            ],

            // Inside "{expr}" string interpolation, tokenize the contents as code
            interpolation: [
                [/{/, 'string', '@push'],
                [/}/, 'string', '@pop'],
                {include: '@root'},
            ],
        },
    };
}

function configuration(): monaco.languages.LanguageConfiguration {
    return {
        comments: {
            lineComment: '//',
        },

        brackets: [
            ['{', '}'],
            ['[', ']'],
            ['(', ')'],
        ],

        autoClosingPairs: [
            {open: '{', close: '}'},
            {open: '[', close: ']'},
            {open: '(', close: ')'},
            {open: '"', close: '"', notIn: ['string']},
        ],

        surroundingPairs: [
            {open: '{', close: '}'},
            {open: '[', close: ']'},
            {open: '(', close: ')'},
            {open: '"', close: '"'},
        ],
    };
}

monaco.languages.register({id: 'contract'});
monaco.languages.setMonarchTokensProvider('contract', definition());
monaco.languages.setLanguageConfiguration('contract', configuration());
