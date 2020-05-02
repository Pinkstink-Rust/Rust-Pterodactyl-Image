#!/usr/bin/env node

let startupCmd = "";
const fs = require("fs");
const WebSocket = require("ws");

if (!fs.existsSync('logs'))
    fs.mkdirSync('logs');

const logFileName = `logs/RustServer-${Date.now()}.log`;
let writeStream = fs.createWriteStream(logFileName);

const logToFile = (message) => {
    if (!writeStream.writable) {
        writeStream.close()
        writeStream = fs.createWriteStream(logFileName);
    }

    writeStream.write(message + "\n", (error) => {
        if (error) {
            console.error("Callback error in appendFile", error);
            err = true;
        }
    });
}

let args = process.argv.splice(process.execArgv.length + 2);
for (let i = 0; i < args.length; i++) {
    if (i === args.length - 1) {
        startupCmd += args[i];
    } else {
        startupCmd += args[i] + " ";
    }
}

if (startupCmd.length < 1) {
    log("Error: Please specify a startup command.");
    process.exit();
}

const seenPercentage = {};
let serverStarted = false;
function filter(data) {
    // Prevent filename output spam.
    const str = data.toString().replace(/\(Filename: .*\)[\n]?/g, '').trim();
    if (str.length < 1)
        return;

    if (str.startsWith("Loading Prefab Bundle ")) { // Rust seems to spam the same percentage, so filter out any duplicates.
        const percentage = str.substr("Loading Prefab Bundle ".length);
        if (seenPercentage[percentage]) return;

        seenPercentage[percentage] = true;
    }

    if (str.startsWith("Server startup complete")) {
        serverStarted = true;
    }

    console.log(str);
}

let exec = require("child_process").exec;
console.log("Starting Rust...");

let exited = false;
const gameProcess = exec(startupCmd);
gameProcess.stdout.on('data', logToFile);
gameProcess.stdout.on('data', filter);

gameProcess.stderr.on('data', logToFile);
gameProcess.stderr.on('data', filter);

gameProcess.on('exit', function (code, signal) {
    exited = true;

    if (code) {
        console.log("Main game process exited with code " + code);
        logToFile("Main game process exited with code " + code);
        // process.exit(code);
    }
});

function initialListener(data) {
    const command = data.toString().trim();
    if (command === 'quit') {
        gameProcess.kill('SIGTERM');
    } else {
        console.log('Unable to run "' + command + '" due to RCON not being connected yet.');
    }
}
process.stdin.resume();
process.stdin.setEncoding("utf8");
process.stdin.on('data', initialListener);

process.on('exit', function (code) {
    if (exited) return;

    console.log("Received request to stop the process, stopping the game...");
    gameProcess.kill('SIGTERM');
});

let waiting = true;
let consoleAttached = true;
const poll = function () {
    function createPacket(command) {
        const packet = {
            Identifier: -1,
            Message: command,
            Name: "WebRcon"
        };
        return JSON.stringify(packet);
    }

    function detachConsole() {
        gameProcess.stdout.removeListener('data', filter);
        consoleAttached = false;
    }

    const serverHostname = "localhost";
    const serverPort = process.env.RCON_PORT;
    const serverPassword = process.env.RCON_PASS;
    const ws = new WebSocket("ws://" + serverHostname + ":" + serverPort + "/" + serverPassword);

    ws.on("open", function open() {
        console.log("Connected to RCON.");
        waiting = false;

        // Hack to fix broken console output
        ws.send(createPacket('status'));

        process.stdin.removeListener('data', initialListener);
        process.stdin.on('data', function (text) {
            ws.send(createPacket(text));
        });
    });

    ws.on("message", function (data, flags) {
        if (!serverStarted) return;

        try {
            let json = JSON.parse(data);
            if (json !== undefined) {
                if (json.Message !== undefined && json.Message.length > 0) {
                    if (serverStarted && consoleAttached) detachConsole();
                    console.log(json.Message);
                }
            } else {
                console.log("Error: Invalid JSON received");
            }
        } catch (e) {
            if (e) {
                console.log(e);
            }
        }
    });

    ws.on("error", function (err) {
        waiting = true;
        setTimeout(poll, 5000);
    });

    ws.on("close", function () {
        if (!waiting) {
            console.log("Connection to server closed.");

            exited = true;
            process.exit();
        }
    });
}
poll();
