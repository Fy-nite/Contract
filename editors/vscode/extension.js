// Contract Language Support — VS Code client.
//
// Starts the Contract language server via the CLI tool (`ccl lsp`), which is
// installed as a global dotnet tool (install.ps1 → `dotnet tool install -g cclc`)
// and therefore on PATH. Override with the contract.languageServer.path setting.

'use strict';

const vscode = require('vscode');
const {
  LanguageClient,
  TransportKind,
  RevealOutputChannelOn,
} = require('vscode-languageclient/node');

/** @type {LanguageClient | undefined} */
let client;

function activate(context) {
  // Custom command for CodeLens "N references" clicks.
  context.subscriptions.push(
    vscode.commands.registerCommand('contract.showReferences', (uri, position, locations) => {
      const docUri = typeof uri === 'string' ? vscode.Uri.parse(uri) : uri;
      const pos = new vscode.Position(position.line, position.character);
      const locs = locations.map(loc => {
        const locUri = typeof loc.uri === 'string' ? vscode.Uri.parse(loc.uri) : loc.uri;
        return new vscode.Location(locUri, new vscode.Range(
          loc.range.start.line, loc.range.start.character,
          loc.range.end.line, loc.range.end.character,
        ));
      });
      vscode.commands.executeCommand('editor.action.showReferences', docUri, pos, locs);
    })
  );

  // Register debug adapter descriptor factory
  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory('contract', {
      createDebugAdapterDescriptor(session) {
        const configured = vscode.workspace
          .getConfiguration('contract')
          .get('debugger.path', '');
        const command = configured || 'ccl';
        return new vscode.DebugAdapterExecutable(command, ['debug']);
      }
    })
  );

  const clientOptions = {
    documentSelector: [
      { language: 'contract', scheme: 'file' },
      { language: 'contract', scheme: 'untitled' },
    ],
    synchronize: {
      configurationSection: 'contract',
    },
    outputChannelName: 'Contract Language Server',
    revealOutputChannelOn: RevealOutputChannelOn.Never,
  };

  client = new LanguageClient(
    'contract',
    'Contract Language Server',
    makeServerOptions(),
    clientOptions
  );

  context.subscriptions.push(client.start());
}

function makeServerOptions() {
  const configured = vscode.workspace
    .getConfiguration('contract')
    .get('languageServer.path', '');
  const command = configured || 'ccl';

  return {
    run: { command, args: ['lsp'], transport: TransportKind.stdio },
    debug: { command, args: ['lsp', '--trace'], transport: TransportKind.stdio },
  };
}

function deactivate() {
  if (!client) return undefined;
  return client.stop();
}

module.exports = { activate, deactivate };
