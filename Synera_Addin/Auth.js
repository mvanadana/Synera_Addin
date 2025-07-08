const express = require('express');
const axios = require('axios');
const open = require('open');
const app = express();

const CLIENT_ID = 'YOUR_CLIENT_ID';
const CLIENT_SECRET = 'YOUR_CLIENT_SECRET';
const REDIRECT_URI = 'http://localhost:8080/callback';
const SCOPES = 'data:read data:write data:create viewables:read';

let accessToken = '';

app.get('/auth', async (req, res) => {
    const authUrl = `https://developer.api.autodesk.com/authentication/v1/authorize?response_type=code&client_id=${CLIENT_ID}&redirect_uri=${encodeURIComponent(REDIRECT_URI)}&scope=${encodeURIComponent(SCOPES)}`;
    await open(authUrl);
    res.send('Redirecting to Autodesk login...');
});

app.get('/callback', async (req, res) => {
    const code = req.query.code;
    const result = await axios.post('https://developer.api.autodesk.com/authentication/v1/gettoken', null, {
        params: {
            client_id: CLIENT_ID,
            client_secret: CLIENT_SECRET,
            grant_type: 'authorization_code',
            code,
            redirect_uri: REDIRECT_URI,
        },
    });

    accessToken = result.data.access_token;
    console.log('✅ Access token:', accessToken);
    res.send('Authentication successful! Token is in console.');
});

app.listen(3000, () => {
    console.log('🌐 Listening on http://localhost:3000/auth');
});
